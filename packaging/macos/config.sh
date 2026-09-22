#!/usr/bin/env bash
# Shared constants for macOS packaging. Version is read from MSBuild (Directory.Build.props).

APP_DISPLAY_NAME="Steam Desktop Authenticator"
APP_EXEC_NAME="SDA.Desktop"
BUNDLE_ID="io.github.nardo021.SteamDesktopAuthenticator"
LS_MINIMUM_SYSTEM_VERSION="12.0"
ICON_FILE_NAME="AppIcon"
PRODUCT_SLUG="Steam-Desktop-Authenticator-macOS"

PACKAGING_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$PACKAGING_DIR/../.." && pwd)"
DIST_DIR="${SDA_DIST_DIR:-$REPO_ROOT/dist}"
DESKTOP_PROJECT="$REPO_ROOT/src/SDA.Desktop/SDA.Desktop.csproj"
ENTITLEMENTS="$PACKAGING_DIR/SDA.entitlements"
INFO_PLIST_TEMPLATE="$PACKAGING_DIR/Info.plist.template"
ICON_SOURCE="$REPO_ROOT/icon.png"

read_product_version() {
  dotnet msbuild "$DESKTOP_PROJECT" -nologo -getProperty:Version | tr -d '\r' | tail -n 1
}

expected_file_arch() {
  local rid="$1"
  case "$rid" in
    osx-arm64) echo "arm64" ;;
    osx-x64) echo "x86_64" ;;
    *)
      echo "unsupported RID: $rid" >&2
      return 2
      ;;
  esac
}

arch_label() {
  local rid="$1"
  case "$rid" in
    osx-arm64) echo "arm64" ;;
    osx-x64) echo "x64" ;;
    *)
      echo "unsupported RID: $rid" >&2
      return 2
      ;;
  esac
}

app_bundle_path() {
  local rid="$1"
  echo "$DIST_DIR/app/$rid/${APP_DISPLAY_NAME}.app"
}

dmg_path() {
  local rid="$1"
  local version
  version="$(read_product_version)"
  echo "$DIST_DIR/${PRODUCT_SLUG}-${version}-$(arch_label "$rid").dmg"
}

require_rid() {
  local rid="${1:-}"
  if [[ "$rid" != "osx-arm64" && "$rid" != "osx-x64" ]]; then
    echo "usage: $0 osx-arm64|osx-x64" >&2
    exit 2
  fi
}

verify_executable_arch() {
  local executable="$1"
  local rid="$2"
  local expected
  expected="$(expected_file_arch "$rid")"
  local info
  info="$(file "$executable")"
  if [[ "$expected" == "arm64" ]] && ! echo "$info" | grep -q "arm64"; then
    echo "architecture mismatch for $executable: expected arm64, got: $info" >&2
    exit 1
  fi
  if [[ "$expected" == "x86_64" ]] && ! echo "$info" | grep -Eq "x86_64"; then
    echo "architecture mismatch for $executable: expected x86_64, got: $info" >&2
    exit 1
  fi
  if command -v lipo >/dev/null 2>&1; then
    local arches
    arches="$(lipo -archs "$executable" 2>/dev/null || true)"
    if [[ -n "$arches" && "$arches" != "$expected" ]]; then
      echo "lipo archs for $executable were '$arches', expected '$expected'" >&2
      exit 1
    fi
  fi
}
