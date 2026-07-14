#!/usr/bin/env bash
# Tear apart local_api_docs/ScriptHookVDotNet3.xml (SHVDN v3 .NET XML doc) into
# REAL sub-XML files (NOT a JSON reformat) that mirror the original XML structure
# one-to-one:
#   - <assembly>  -> assembly.xml                  (the original <assembly> element)
#   - <members>   -> members/                      (a directory mirroring <members>)
#       - <member> -> members/<K>__<Name>.xml      (one file per <member>, with the
#                                                 original <member> element verbatim)
#   - index.json                                  (member-name -> relative-path)
#
# This is deliberately NOT modeled after the parse_natives.sh / gen.json layout
# (which used by_namespace/<NS>/<name>.json). Here the folder structure IS the
# XML structure: 1 dir for <assembly>, 1 dir for <members>, 1 file per <member>.
# The agent can read these files directly with any XML parser.
#
# Each .xml file is a valid standalone XML document with a `<?xml ?>` declaration
# and the <member> element pretty-printed with 4-space indent (matches source).
#
# Usage: parse_local_api_docs.sh [-s SOURCE] [-o OUT_DIR] [-k]
#   -s, --source PATH   Legacy XML doc (default: <project root>/local_api_docs/ScriptHookVDotNet3.xml)
#   -o, --out-dir DIR   Output directory (default: <SourceDir>/parsed)
#   -k, --keep          Don't clean OutDir before writing
#   -h, --help          Print this help and exit
set -euo pipefail

die() { echo "ERROR: $*" >&2; exit 1; }
note() { echo "==> $*"; }

ProjectRoot="$(cd "$(dirname "$0")/.." && pwd)"
Source=""
OutDir=""
KeepOut=0

while [ $# -gt 0 ]; do
  case "$1" in
    -s|--source)  [ $# -ge 2 ] || die "option $1 cần tham số PATH"; Source="$2"; shift 2 ;;
    -o|--out-dir) [ $# -ge 2 ] || die "option $1 cần tham số DIR"; OutDir="$2"; shift 2 ;;
    -k|--keep)    KeepOut=1; shift ;;
    -h|--help)    sed -n '3,24p' "$0"; exit 0 ;;
    *)            die "tham số lạ: $1 (gõ --help để xem cách dùng)" ;;
  esac
done

DefaultSource="$ProjectRoot/local_api_docs/ScriptHookVDotNet3.xml"
[ -n "$Source" ] || Source="$DefaultSource"
[ -n "$OutDir" ] || OutDir="$(dirname "$Source")/parsed"

# Pre-flight
[ -f "$Source" ] || die "Không tìm thấy source: '$Source'."

# Clean output unless -k
if [ -d "$OutDir" ] && [ "$KeepOut" -eq 0 ]; then
  note "Cleaning $OutDir"
  rm -rf "$OutDir"
fi
mkdir -p "$OutDir"

note "Parsing $Source -> $OutDir"
python3 - "$Source" "$OutDir" <<'PYEOF'
import json, os, sys, hashlib
import xml.etree.ElementTree as ET
import xml.dom.minidom as minidom

src_path, out_dir = sys.argv[1], sys.argv[2]

def write_pretty_xml(element, out_file):
    """Serialize a single XML element as a standalone, pretty-printed XML file.
    4-space indent matches the source XML's indentation style."""
    rough = ET.tostring(element, encoding='utf-8')
    reparsed = minidom.parseString(rough)
    pretty = reparsed.toprettyxml(indent='    ', encoding='utf-8')
    with open(out_file, 'wb') as f:
        f.write(pretty)

tree = ET.parse(src_path)
root = tree.getroot()

# --- <assembly> -> assembly.xml ----------------------------------------------
assembly = root.find('assembly')
if assembly is not None:
    asm_path = os.path.join(out_dir, 'assembly.xml')
    write_pretty_xml(assembly, asm_path)
    print('  assembly.xml: wrote <assembly> element')

# --- <members> -> members/<K>__<Name>.xml -----------------------------------
members = root.find('members')
if members is None:
    print('No <members> element found in source', file=sys.stderr)
    sys.exit(1)

members_dir = os.path.join(out_dir, 'members')
os.makedirs(members_dir, exist_ok=True)

index = {}
seen = set()
count = 0
skipped = 0

for m in members.findall('member'):
    name = m.get('name', '')
    if not name or ':' not in name:
        skipped += 1
        continue
    kind, rest = name.split(':', 1)
    kind = kind.strip()
    rest = rest.strip()
    if kind not in ('T', 'M', 'P', 'F', 'E'):
        kind = 'X'
    # The "rest" is "Namespace.TypeName.MemberName" (or "Namespace.TypeName.MemberName(Params)"
    # for methods). Split on the LAST '.' to isolate the member name.
    last_dot = rest.rfind('.')
    if last_dot <= 0:
        skipped += 1
        continue
    member_name = rest[last_dot + 1:]
    if kind == 'M':
        paren = member_name.find('(')
        if paren > 0:
            member_name = member_name[:paren]
    # Sanitize generic backticks for filesystem-safe names: ``Call`1`` -> ``Call_1``
    member_name = member_name.replace('`', '_')

    base = f'{kind}__{member_name}'
    if base in seen:
        h = hashlib.md5(name.encode('utf-8')).hexdigest()[:8]
        base = f'{base}_{h}'
    seen.add(base)

    out_file = os.path.join(members_dir, f'{base}.xml')
    write_pretty_xml(m, out_file)
    index[name] = f'members/{base}.xml'
    count += 1

# --- index.json (auxiliary; the .xml files are the source of truth) ----------
index_path = os.path.join(out_dir, 'index.json')
with open(index_path, 'w', encoding='utf-8') as f:
    json.dump(index, f, ensure_ascii=False, indent=2)

print(f'Done. members={count} skipped={skipped} out={out_dir}')
PYEOF
