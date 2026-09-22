#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

RIDS=("osx-arm64" "osx-x64")
if [[ "${1:-}" == "osx-arm64" || "${1:-}" == "osx-x64" ]]; then
  RIDS=("$1")
fi

for rid in "${RIDS[@]}"; do
  "$SCRIPT_DIR/build-app.sh" "$rid"
  "$SCRIPT_DIR/build-dmg.sh" "$rid"
  "$SCRIPT_DIR/verify-release.sh" "$rid" --unsigned
done

"$SCRIPT_DIR/write-checksums.sh"
echo "Unsigned packaging complete in $DIST_DIR"
echo "v1.0.0 does not sign or notarize. sign-app.sh / notarize.sh remain optional for a later release."
