#!/usr/bin/env bash
# Cập nhật corpus: git pull --ff-only cho 3 repo trong api_docs/.
# Chạy sau bootstrap_api_docs.sh lần đầu; tương đương update_api_docs.ps1
# (PowerShell, Windows), nhưng chạy được trên Linux / WSL / macOS.
#
# Usage: update_api_docs.sh [-d DIR]
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
      sed -n '3,8p' "$0"; exit 0 ;;
    *)
      die "tham số lạ: $1 (gõ --help để xem cách dùng)" ;;
  esac
done

[ -n "$ApiDocsRoot" ] || ApiDocsRoot="$(cd "$(dirname "$0")/.." && pwd)/api_docs"

# --- Vòng cập nhật cho từng repo ----------------------------------------
Repos=(scripthookvdotnet scripthookvdotnet.wiki gta5-nativedb-data)
for r in "${Repos[@]}"; do
  dest="$ApiDocsRoot/$r"
  if [ ! -d "$dest/.git" ]; then
    die "thiếu repository: $dest — chạy scripts/bootstrap_api_docs.sh (PowerShell: scripts/bootstrap_api_docs.ps1) trước."
  fi
  note "Updating $dest"
  git -C "$dest" pull --ff-only
done

note "Done."
