#!/usr/bin/env bash
# Tear apart local_api_docs/ScriptHookVDotNet3.xml (SHVDN v3 .NET XML doc) into
# per-member JSON files under <SourceDir>/parsed/, organized by namespace+type,
# plus a root index.json for quick member-name lookups.
#
# Output layout:
#   <OutDir>/
#   ├── index.json
#   └── by_namespace/
#       └── <NS>/
#           └── <TypeName>/
#               └── <Kind>__<MemberName>.json   (one file per <member>)
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
    -h|--help)    sed -n '3,17p' "$0"; exit 0 ;;
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
import json, os, sys, re, hashlib
import xml.etree.ElementTree as ET

FORBIDDEN = re.compile(r'[<>:"/\\|?*]')
MAX_LEN = 120
src_path, out_dir = sys.argv[1], sys.argv[2]

def safe(s, maxlen=MAX_LEN):
    if s is None:
        return None
    s = str(s).strip()
    if not s:
        return None
    s = FORBIDDEN.sub('_', s)
    if len(s) > maxlen:
        h = hashlib.md5(s.encode('utf-8')).hexdigest()[:8]
        s = s[:maxlen-9] + '_' + h
    return s

tree = ET.parse(src_path)
root = tree.getroot()
members = root.find('members')
if members is None:
    print('No <members> element found in source', file=sys.stderr)
    sys.exit(1)

index = {}
count = 0
skipped = 0
seen = set()

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
    parts = rest.split('.')
    if len(parts) < 2:
        skipped += 1
        continue
    member_name = parts[-1]
    if kind == 'M':
        paren = member_name.find('(')
        if paren > 0:
            member_name = member_name[:paren]
    type_parts = parts[:-1]
    type_name = type_parts[-1]
    ns = '.'.join(type_parts[:-1]) if len(type_parts) > 1 else ''

    ns_safe = safe(ns) or '_root'
    type_safe = safe(type_name) or '_anon'
    member_safe = safe(member_name) or '_anon'

    rel_dir = f'by_namespace/{ns_safe}/{type_safe}'
    base = f'{kind}__{member_safe}'
    collision_key = f'{rel_dir}/{base}'
    if collision_key in seen:
        h = hashlib.md5(name.encode('utf-8')).hexdigest()[:8]
        base = f'{base}_{h}'
    seen.add(collision_key)

    rel_path = f'{rel_dir}/{base}.json'
    out_file = os.path.join(out_dir, rel_path)
    os.makedirs(os.path.dirname(out_file), exist_ok=True)

    def text_of(tag):
        el = m.find(tag)
        if el is None: return None
        t = (el.text or '').strip()
        return t if t else None

    entry = {
        'name': name,
        'kind': kind,
        'namespace': ns,
        'typeName': type_name,
        'memberName': member_name,
    }
    s = text_of('summary')
    if s: entry['summary'] = s
    s = text_of('remarks')
    if s: entry['remarks'] = s
    s = text_of('returns')
    if s: entry['returns'] = s
    s = text_of('value')
    if s: entry['value'] = s
    params = []
    for p in m.findall('param'):
        params.append({
            'name': p.get('name', ''),
            'description': (p.text or '').strip(),
        })
    if params:
        entry['params'] = params
    exceptions = []
    for e in m.findall('exception'):
        exceptions.append({
            'cref': e.get('cref', ''),
            'description': (e.text or '').strip(),
        })
    if exceptions:
        entry['exceptions'] = exceptions

    with open(out_file, 'w', encoding='utf-8') as f:
        json.dump(entry, f, ensure_ascii=False, indent=2)
    index[name] = rel_path
    count += 1

with open(os.path.join(out_dir, 'index.json'), 'w', encoding='utf-8') as f:
    json.dump(index, f, ensure_ascii=False, indent=2)

print(f'Done. members={count} skipped={skipped} out={out_dir}')
PYEOF
