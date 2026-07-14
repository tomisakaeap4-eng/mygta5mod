#!/usr/bin/env bash
# Tear apart api_docs/gta5-nativedb-data/natives.json (legacy aggregated file) into
# per-native JSON files under <SourceDir>/natives_parsed/, organized by namespace,
# plus a root index.json for quick hash lookups.
#
# Output layout:
#   <OutDir>/
#   ├── index.json
#   └── by_namespace/
#       └── <NS>/
#           └── <NAME-or-HASH>.json   (one file per native)
#
# Usage: parse_natives.sh [-s SOURCE] [-o OUT_DIR] [-k]
#   -s, --source PATH   Legacy natives.json (default: <project root>/api_docs/gta5-nativedb-data/natives.json)
#   -o, --out-dir DIR   Output directory (default: <SourceDir>/natives_parsed)
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

DefaultSource="$ProjectRoot/api_docs/gta5-nativedb-data/natives.json"
[ -n "$Source" ] || Source="$DefaultSource"
[ -n "$OutDir" ] || OutDir="$(dirname "$Source")/natives_parsed"

# Pre-flight
[ -f "$Source" ] || die "Không tìm thấy source: '$Source'. Hãy chạy scripts/bootstrap_api_docs.sh trước (hoặc dùng --source)."

# Clean output unless -k
if [ -d "$OutDir" ] && [ "$KeepOut" -eq 0 ]; then
  note "Cleaning $OutDir"
  rm -rf "$OutDir"
fi
mkdir -p "$OutDir"

# Heavy lifting: python handles JSON safely
note "Parsing $Source -> $OutDir"
python3 - "$Source" "$OutDir" <<'PYEOF'
import json, os, sys, re

FORBIDDEN = re.compile(r'[<>:"/\\|?*]')
src_path, out_dir = sys.argv[1], sys.argv[2]

def safe(s):
    if s is None:
        return None
    s = str(s).strip()
    if not s:
        return None
    return FORBIDDEN.sub('_', s).upper()

with open(src_path, encoding='utf-8') as f:
    data = json.load(f)

by_ns_dir = os.path.join(out_dir, 'by_namespace')
os.makedirs(by_ns_dir, exist_ok=True)

index = {}
ns_count = 0
native_count = 0
skipped = 0

for ns_raw, ns_value in data.items():
    ns_safe = safe(ns_raw)
    if not ns_safe:
        continue
    ns_dir = os.path.join(by_ns_dir, ns_safe)
    os.makedirs(ns_dir, exist_ok=True)
    ns_count += 1
    name_seen = set()

    if not isinstance(ns_value, dict):
        skipped += 1
        continue

    for hash_key, entry in ns_value.items():
        if not isinstance(entry, dict):
            skipped += 1
            continue
        name = entry.get('name')
        file_safe = None
        if name and name != hash_key:
            file_safe = safe(name)
        if not file_safe:
            file_safe = safe(hash_key)
        if not file_safe:
            skipped += 1
            continue
        original = file_safe
        if file_safe in name_seen:
            short_hash = hash_key[2:] if hash_key.lower().startswith('0x') else hash_key
            file_safe = f'{original}_{short_hash}'
        name_seen.add(file_safe)

        new_entry = {'hash': hash_key, 'namespace': ns_raw}
        new_entry.update(entry)

        out_file = os.path.join(ns_dir, file_safe + '.json')
        with open(out_file, 'w', encoding='utf-8') as f:
            json.dump(new_entry, f, ensure_ascii=False, indent=2)
        index[hash_key] = f'by_namespace/{ns_safe}/{file_safe}.json'
        native_count += 1

with open(os.path.join(out_dir, 'index.json'), 'w', encoding='utf-8') as f:
    json.dump(index, f, ensure_ascii=False, indent=2)

print(f'Done. namespaces={ns_count} natives={native_count} skipped={skipped} out={out_dir}')
PYEOF
