#!/usr/bin/env bash
# Parse local ScriptHookVDotNet and LemonUI XML docs into compact lookup trees.
# Usage: parse_local_api_docs.sh [-s SOURCE] [-o OUT_DIR] [-k]
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

parser="$project_root/scripts/parse_api_docs.py"
local_api_docs_root="$project_root/local_api_docs"
parsed_root="$local_api_docs_root/parsed"

command -v python3 >/dev/null 2>&1 || die "python3 is required"
[ -f "$parser" ] || die "missing shared parser: $parser"

doc_id_from_source() {
  basename "$1" .xml \
    | tr '[:upper:]' '[:lower:]' \
    | sed -E 's/[^a-z0-9]+/-/g; s/^-+//; s/-+$//' \
    | sed -E 's/^$/local-api-doc/'
}

parse_one() {
  local doc_source="$1"
  local doc_out_dir="$2"
  [ -f "$doc_source" ] || die "missing local API XML: $doc_source"

  echo "Parsing local API XML: $doc_source -> $doc_out_dir"
  local arguments=("$parser" local-xml --source "$doc_source" --out-dir "$doc_out_dir")
  [ "$keep_out" -eq 0 ] || arguments+=(--keep-out)
  python3 "${arguments[@]}"
}

if [ -n "$source" ] || [ -n "$out_dir" ]; then
  source="${source:-$local_api_docs_root/ScriptHookVDotNet3.xml}"
  out_dir="${out_dir:-$parsed_root/$(doc_id_from_source "$source")}"
  parse_one "$source" "$out_dir"
else
  parse_one "$local_api_docs_root/ScriptHookVDotNet3.xml" "$parsed_root/scripthookvdotnet3"
  parse_one "$local_api_docs_root/LemonUI.SHVDN3.xml" "$parsed_root/lemonui-shvdn3"
fi
