#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

mkdir -p "$DIST_DIR"
cd "$DIST_DIR"
shopt -s nullglob
dmgs=(*.dmg)
if [[ ${#dmgs[@]} -eq 0 ]]; then
  echo "no DMG files in $DIST_DIR" >&2
  exit 1
fi

shasum -a 256 "${dmgs[@]}" | sort -k2 > SHA256SUMS.txt
echo "Wrote $DIST_DIR/SHA256SUMS.txt"
cat SHA256SUMS.txt
