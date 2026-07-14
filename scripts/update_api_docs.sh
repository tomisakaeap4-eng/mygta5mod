#!/usr/bin/env bash
# Fast-forward the three API repositories in <project root>/api_docs.
# Usage: update_api_docs.sh [-d DIR]
set -euo pipefail

die() { echo "ERROR: $*" >&2; exit 1; }
note() { echo "==> $*"; }

api_docs_root=""
while [ $# -gt 0 ]; do
  case "$1" in
    -d|--dir) [ $# -ge 2 ] || die "option $1 requires DIR"; api_docs_root="$2"; shift 2 ;;
    -h|--help) sed -n '1,3p' "$0"; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

command -v git >/dev/null 2>&1 || die "git is required"
project_root="$(cd "$(dirname "$0")/.." && pwd)"
api_docs_root="${api_docs_root:-$project_root/api_docs}"

for repository in scripthookvdotnet scripthookvdotnet.wiki gta5-nativedb-data; do
  destination="$api_docs_root/$repository"
  [ -d "$destination/.git" ] || die "missing corpus repository: $destination; run bootstrap_api_docs.sh first"
  note "Updating $destination"
  git -C "$destination" pull --ff-only
done
note "Corpus is up to date at: $api_docs_root"
