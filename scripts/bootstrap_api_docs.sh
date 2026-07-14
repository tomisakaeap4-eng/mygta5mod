#!/usr/bin/env bash
# Bootstrap corpus cho agent: clone shallow 4 repo tham khảo vào api_docs/.
# Tương đương bootstrap_api_docs.ps1 (PowerShell, Windows), nhưng chạy được
# trên Linux / WSL / macOS — phù hợp với dev box của agent.
#
# Usage: bootstrap_api_docs.sh [-d DIR]
#   -d, --dir DIR   Thư mục corpus (mặc định: ./api_docs cạnh script)
#   -h, --help      In phần hướng dẫn này rồi thoát
set -euo pipefail

die() { echo "ERROR: $*" >&2; exit 1; }
note() { echo "==> $*"; }

# --- Parse arguments -------------------------------------------------------
ApiDocsRoot=""
while [ $# -gt 0 ]; do
  case "$1" in
    -d|--dir)
      [ $# -ge 2 ] || die "option $1 cần tham số DIR"
      ApiDocsRoot="$2"; shift 2 ;;
    -h|--help)
      sed -n '3,9p' "$0"; exit 0 ;;
    *)
      die "tham số lạ: $1 (gõ --help để xem cách dùng)" ;;
  esac
done

ProjectRoot="$(cd "$(dirname "$0")/.." && pwd)"
[ -n "$ApiDocsRoot" ] || ApiDocsRoot="$ProjectRoot/api_docs"

# --- Pre-flight -----------------------------------------------------------
command -v git >/dev/null 2>&1 || die "không tìm thấy 'git'; cài git rồi chạy lại"

# Chuẩn bị thư mục: corpus
mkdir -p "$ApiDocsRoot"

# --- Clone-or-update helper ----------------------------------------------
clone_or_update() {
  local url="$1" dest="$2" branch="$3"
  if [ -d "$dest/.git" ]; then
    note "Updating $dest"
    git -C "$dest" fetch --all --prune
    git -C "$dest" checkout "$branch"
    git -C "$dest" pull --ff-only origin "$branch"
  else
    note "Cloning $url -> $dest"
    git clone --depth 1 --branch "$branch" "$url" "$dest"
  fi
}

# --- Clone 4 repo tham khảo cho agent ------------------------------------
clone_or_update "https://github.com/scripthookvdotnet/scripthookvdotnet.git"      "$ApiDocsRoot/scripthookvdotnet"      "main"
clone_or_update "https://github.com/scripthookvdotnet/scripthookvdotnet.wiki.git" "$ApiDocsRoot/scripthookvdotnet.wiki" "master"
clone_or_update "https://github.com/alloc8or/gta5-nativedb-data.git"            "$ApiDocsRoot/gta5-nativedb-data"      "master"
clone_or_update "https://github.com/acidlabsdev/gtav-legacy-scripts.git"      "$ApiDocsRoot/gtav-legacy-scripts"      "main"

note "Done. Corpus ở: $ApiDocsRoot"
