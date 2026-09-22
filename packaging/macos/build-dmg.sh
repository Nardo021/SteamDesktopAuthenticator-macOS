#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

RID="${1:-}"
require_rid "$RID"

VERSION="$(read_product_version)"
APP_DIR="$(app_bundle_path "$RID")"
if [[ ! -d "$APP_DIR" ]]; then
  echo "app bundle not found: $APP_DIR" >&2
  echo "run: $SCRIPT_DIR/build-app.sh $RID" >&2
  exit 1
fi

STAGE="$DIST_DIR/dmg-stage/$RID"
DMG="$(dmg_path "$RID")"
VOLNAME="$APP_DISPLAY_NAME $(arch_label "$RID")"

rm -rf "$STAGE"
mkdir -p "$STAGE"
ditto "$APP_DIR" "$STAGE/${APP_DISPLAY_NAME}.app"
ln -s /Applications "$STAGE/Applications"

mkdir -p "$(dirname "$DMG")"
rm -f "$DMG"
hdiutil create \
  -volname "$VOLNAME" \
  -srcfolder "$STAGE" \
  -ov \
  -format UDZO \
  "$DMG" >/dev/null

echo "Created $DMG"
