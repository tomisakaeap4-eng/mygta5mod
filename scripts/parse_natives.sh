#!/usr/bin/env bash
# Parse NativeDB Legacy natives.json with the shared, validated Python parser.
# Usage: parse_natives.sh [-s SOURCE] [-o OUT_DIR] [-k]
set -euo pipefail

die() { echo "ERROR: $*" >&2; exit 1; }

project_root="$(cd "$(dirname "$0")/.." && pwd)"
source=""
out_dir=""
keep_out=0

while [ $# -gt 0 ]; do
  case "$1" in
    -s|--source) [ $# -ge 2 ] || die "option $1 requires PATH"; source="$2"; shift 2 ;;
    -o|--out-dir) [ $# -ge 2 ] || die "option $1 requires PATH"; out_dir="$2"; shift 2 ;;
    -k|--keep-out) keep_out=1; shift ;;
    -h|--help)
      sed -n '1,4p' "$0"
      exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

source="${source:-$project_root/api_docs/gta5-nativedb-data/natives.json}"
out_dir="${out_dir:-$(dirname "$source")/natives_parsed}"
parser="$project_root/scripts/parse_api_docs.py"

command -v python3 >/dev/null 2>&1 || die "python3 is required"
[ -f "$parser" ] || die "missing shared parser: $parser"

arguments=("$parser" natives --source "$source" --out-dir "$out_dir")
[ "$keep_out" -eq 0 ] || arguments+=(--keep-out)
python3 "${arguments[@]}"
