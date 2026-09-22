#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

RID="${1:-}"
require_rid "$RID"

VERSION="$(read_product_version)"
if [[ -z "$VERSION" ]]; then
  echo "failed to read Version from $DESKTOP_PROJECT" >&2
  exit 1
fi

PUBLISH_DIR="$DIST_DIR/publish/$RID"
APP_DIR="$(app_bundle_path "$RID")"
CONTENTS="$APP_DIR/Contents"

echo "Building $APP_DISPLAY_NAME $VERSION ($RID)"
rm -rf "$PUBLISH_DIR" "$APP_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$DESKTOP_PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:UseAppHost=true \
  -p:PublishTrimmed=false \
  -p:PublishAot=false \
  -o "$PUBLISH_DIR"

EXECUTABLE="$PUBLISH_DIR/$APP_EXEC_NAME"
if [[ ! -f "$EXECUTABLE" ]]; then
  echo "publish output is missing $APP_EXEC_NAME" >&2
  exit 1
fi
chmod +x "$EXECUTABLE"
verify_executable_arch "$EXECUTABLE" "$RID"

ICNS="$DIST_DIR/AppIcon.icns"
"$SCRIPT_DIR/generate-icns.sh" "$ICNS" "$ICON_SOURCE"

mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"
ditto "$PUBLISH_DIR" "$CONTENTS/MacOS"
cp "$ICNS" "$CONTENTS/Resources/${ICON_FILE_NAME}.icns"
chmod +x "$CONTENTS/MacOS/$APP_EXEC_NAME"

sed \
  -e "s|__CFBUNDLE_EXECUTABLE__|${APP_EXEC_NAME}|g" \
  -e "s|__CFBUNDLE_NAME__|${APP_DISPLAY_NAME}|g" \
  -e "s|__CFBUNDLE_DISPLAY_NAME__|${APP_DISPLAY_NAME}|g" \
  -e "s|__CFBUNDLE_IDENTIFIER__|${BUNDLE_ID}|g" \
  -e "s|__CFBUNDLE_VERSION__|${VERSION}|g" \
  -e "s|__CFBUNDLE_SHORT_VERSION__|${VERSION}|g" \
  -e "s|__CFBUNDLE_ICON_FILE__|${ICON_FILE_NAME}|g" \
  -e "s|__LS_MINIMUM_SYSTEM_VERSION__|${LS_MINIMUM_SYSTEM_VERSION}|g" \
  "$INFO_PLIST_TEMPLATE" > "$CONTENTS/Info.plist"

plutil -lint "$CONTENTS/Info.plist" >/dev/null
verify_executable_arch "$CONTENTS/MacOS/$APP_EXEC_NAME" "$RID"

echo "Built $APP_DIR"
