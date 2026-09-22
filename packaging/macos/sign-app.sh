#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

RID="${1:-}"
require_rid "$RID"

if [[ -z "${APPLE_SIGN_IDENTITY:-}" ]]; then
  echo "APPLE_SIGN_IDENTITY is not set." >&2
  echo "Unsigned local packaging does not require this script." >&2
  echo "For Developer ID release signing set e.g.:" >&2
  echo "  export APPLE_SIGN_IDENTITY='Developer ID Application: Name (TEAMID)'" >&2
  exit 2
fi

APP_DIR="$(app_bundle_path "$RID")"
if [[ ! -d "$APP_DIR" ]]; then
  echo "app bundle not found: $APP_DIR" >&2
  exit 1
fi

if [[ ! -f "$ENTITLEMENTS" ]]; then
  echo "entitlements not found: $ENTITLEMENTS" >&2
  exit 1
fi

is_macho() {
  local path="$1"
  file -b "$path" | grep -q "Mach-O"
}

# Sign nested Mach-O files from the deepest path first, then the bundle.
# Do not use codesign --deep as the primary signing strategy.
while IFS= read -r path; do
  if is_macho "$path"; then
    echo "Signing $path"
    codesign --force --options runtime --timestamp \
      --sign "$APPLE_SIGN_IDENTITY" \
      "$path"
  fi
done < <(find "$APP_DIR/Contents" -type f | awk '{ print gsub(/\//, "/") "\t" $0 }' | sort -nr | cut -f2-)

echo "Signing $APP_DIR"
codesign --force --options runtime --timestamp \
  --entitlements "$ENTITLEMENTS" \
  --sign "$APPLE_SIGN_IDENTITY" \
  "$APP_DIR"

codesign --verify --strict --verbose=2 "$APP_DIR"
echo "Signed $APP_DIR"
