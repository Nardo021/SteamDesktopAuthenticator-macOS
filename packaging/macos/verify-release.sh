#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

UNSIGNED=0
RID=""
for arg in "$@"; do
  case "$arg" in
    --unsigned)
      UNSIGNED=1
      ;;
    osx-arm64|osx-x64)
      RID="$arg"
      ;;
    -h|--help)
      echo "usage: $0 osx-arm64|osx-x64 [--unsigned]" >&2
      exit 0
      ;;
    *)
      echo "usage: $0 osx-arm64|osx-x64 [--unsigned]" >&2
      exit 2
      ;;
  esac
done

require_rid "$RID"

APP_DIR="$(app_bundle_path "$RID")"
EXPECTED_ARCH="$(expected_file_arch "$RID")"
EXPECTED_VERSION="$(read_product_version)"
EXECUTABLE="$APP_DIR/Contents/MacOS/$APP_EXEC_NAME"
PLIST="$APP_DIR/Contents/Info.plist"
ICNS="$APP_DIR/Contents/Resources/${ICON_FILE_NAME}.icns"

fail=0
pass() { echo "PASS  $1"; }
info() { echo "INFO  $1"; }
bad() { echo "FAIL  $1"; fail=1; }

if [[ ! -d "$APP_DIR" ]]; then
  bad "bundle exists ($APP_DIR)"
  exit 1
fi
pass "bundle exists"

if [[ ! -f "$PLIST" ]]; then
  bad "Info.plist exists"
else
  if plutil -lint "$PLIST" >/dev/null; then
    pass "Info.plist parses"
  else
    bad "Info.plist parses"
  fi
fi

if [[ ! -f "$EXECUTABLE" ]]; then
  bad "CFBundleExecutable exists ($APP_EXEC_NAME)"
else
  pass "CFBundleExecutable exists"
  if [[ -x "$EXECUTABLE" ]]; then
    pass "executable is executable"
  else
    bad "executable is executable"
  fi
  verify_executable_arch "$EXECUTABLE" "$RID"
  pass "architecture is $EXPECTED_ARCH"
fi

if [[ -f "$ICNS" ]]; then
  pass "application icon packaged"
else
  bad "application icon packaged"
fi

BUNDLE_ID_VALUE="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$PLIST" 2>/dev/null || true)"
DISPLAY_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleDisplayName' "$PLIST" 2>/dev/null || true)"
SHORT_VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$PLIST" 2>/dev/null || true)"
BUNDLE_VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$PLIST" 2>/dev/null || true)"

if [[ "$BUNDLE_ID_VALUE" == "$BUNDLE_ID" ]]; then
  pass "CFBundleIdentifier is $BUNDLE_ID"
else
  bad "CFBundleIdentifier is $BUNDLE_ID (got '$BUNDLE_ID_VALUE')"
fi

if [[ "$BUNDLE_ID_VALUE" == com.example.* ]]; then
  bad "bundle identifier must not use com.example.*"
fi

if [[ "$DISPLAY_NAME" == "$APP_DISPLAY_NAME" ]]; then
  pass "CFBundleDisplayName is $APP_DISPLAY_NAME"
else
  bad "CFBundleDisplayName is $APP_DISPLAY_NAME (got '$DISPLAY_NAME')"
fi

if [[ "$SHORT_VERSION" == "$EXPECTED_VERSION" && "$BUNDLE_VERSION" == "$EXPECTED_VERSION" ]]; then
  pass "version is $EXPECTED_VERSION"
else
  bad "version is $EXPECTED_VERSION (short='$SHORT_VERSION' bundle='$BUNDLE_VERSION')"
fi

if [[ ! -f "$EXECUTABLE" ]]; then
  echo "verification incomplete"
  exit 1
fi

set +e
SIGN_OUT="$(codesign --verify --strict --verbose=2 "$APP_DIR" 2>&1)"
SIGN_STATUS=$?
set -e
if [[ $SIGN_STATUS -eq 0 ]]; then
  if [[ $UNSIGNED -eq 1 ]]; then
    info "bundle currently has a signature; --unsigned requested, not treating that as a v1.0.0 requirement"
  else
    info "code signature present (optional; not required for unsigned public release)"
  fi
else
  echo "Signing: SKIPPED — unsigned release"
fi

echo "Notarization: SKIPPED — unsigned release"

if [[ $UNSIGNED -eq 1 || $SIGN_STATUS -ne 0 ]]; then
  info "Code signing: NOT PROVIDED"
  info "Notarization: NOT PROVIDED"
fi

DMG="$(dmg_path "$RID")"
if [[ -f "$DMG" ]]; then
  pass "DMG exists ($(basename "$DMG"))"
  MOUNT="$(mktemp -d /tmp/sda-verify-dmg.XXXXXX)"
  set +e
  hdiutil attach "$DMG" -nobrowse -readonly -mountpoint "$MOUNT" >/dev/null
  ATTACH_STATUS=$?
  set -e
  if [[ $ATTACH_STATUS -ne 0 ]]; then
    bad "DMG mounts"
    rmdir "$MOUNT" 2>/dev/null || true
  else
    pass "DMG mounts"
    if [[ -d "$MOUNT/${APP_DISPLAY_NAME}.app" ]]; then
      pass "DMG contains ${APP_DISPLAY_NAME}.app"
    else
      bad "DMG contains ${APP_DISPLAY_NAME}.app"
    fi
    if [[ -L "$MOUNT/Applications" || -d "$MOUNT/Applications" ]]; then
      pass "DMG contains Applications symlink"
    else
      bad "DMG contains Applications symlink"
    fi
    hdiutil detach "$MOUNT" >/dev/null || true
    rmdir "$MOUNT" 2>/dev/null || true
  fi
else
  info "DMG not present at $DMG"
fi

if [[ $fail -ne 0 ]]; then
  echo "verify-release: FAILED"
  exit 1
fi
echo "verify-release: unsigned checks passed"
