#!/usr/bin/env bash
# Clone missing API repositories or fast-forward existing ones in <project root>/api_docs.
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
mkdir -p "$api_docs_root"

sync_repository() {
  local url="$1" destination="$2" branch="$3"
  if [ -d "$destination/.git" ]; then
    note "Updating $destination"
    git -C "$destination" fetch --all --prune
    git -C "$destination" checkout "$branch"
    git -C "$destination" pull --ff-only origin "$branch"
  elif [ -e "$destination" ]; then
    die "destination exists but is not a Git repository: $destination"
  else
    note "Cloning $url -> $destination"
    git clone --depth 1 --branch "$branch" "$url" "$destination"
  fi
}

sync_repository 'https://github.com/scripthookvdotnet/scripthookvdotnet.git' "$api_docs_root/scripthookvdotnet" main
sync_repository 'https://github.com/scripthookvdotnet/scripthookvdotnet.wiki.git' "$api_docs_root/scripthookvdotnet.wiki" master
sync_repository 'https://github.com/alloc8or/gta5-nativedb-data.git' "$api_docs_root/gta5-nativedb-data" master
sync_repository 'https://github.com/LemonUIbyLemon/LemonUI.git' "$api_docs_root/lemonui" master
sync_repository 'https://github.com/LemonUIbyLemon/Examples.git' "$api_docs_root/lemonui-examples" master
sync_repository 'https://github.com/LemonUIbyLemon/LemonUI.wiki.git' "$api_docs_root/lemonui-wiki" master
sync_repository 'https://github.com/openai/openai-dotnet.git' "$api_docs_root/openai-dotnet" main
note "Corpus is up to date at: $api_docs_root"
