#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

OUT="${1:-$DIST_DIR/AppIcon.icns}"
SRC="${2:-$ICON_SOURCE}"

if [[ ! -f "$SRC" ]]; then
  echo "icon source not found: $SRC" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT")"
ICONSET="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$ICONSET"

for size in 16 32 64 128 256 512; do
  sips -z "$size" "$size" "$SRC" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  if [[ $double -le 1024 ]]; then
    sips -z "$double" "$double" "$SRC" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
  fi
done

iconutil -c icns "$ICONSET" -o "$OUT"
echo "$OUT"
