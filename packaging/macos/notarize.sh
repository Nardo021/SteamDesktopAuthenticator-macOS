#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=config.sh
source "$SCRIPT_DIR/config.sh"

RID="${1:-}"
require_rid "$RID"

DMG="$(dmg_path "$RID")"
APP_DIR="$(app_bundle_path "$RID")"

if [[ ! -f "$DMG" ]]; then
  echo "DMG not found: $DMG" >&2
  echo "run: $SCRIPT_DIR/build-dmg.sh $RID" >&2
  exit 1
fi

AUTH=()
if [[ -n "${APPLE_NOTARY_PROFILE:-}" ]]; then
  AUTH=(--keychain-profile "$APPLE_NOTARY_PROFILE")
elif [[ -n "${APPLE_API_KEY:-}" && -n "${APPLE_API_KEY_ID:-}" && -n "${APPLE_API_ISSUER:-}" ]]; then
  AUTH=(--key "$APPLE_API_KEY" --key-id "$APPLE_API_KEY_ID" --issuer "$APPLE_API_ISSUER")
else
  echo "Notary credentials are not configured." >&2
  echo "Set APPLE_NOTARY_PROFILE (preferred) or APPLE_API_KEY + APPLE_API_KEY_ID + APPLE_API_ISSUER." >&2
  echo "Do not put Apple credentials in the repository." >&2
  exit 2
fi

LOG_DIR="$DIST_DIR/notary"
mkdir -p "$LOG_DIR"
SUBMIT_LOG="$LOG_DIR/submit-$(arch_label "$RID").log"

echo "Submitting $DMG"
set +e
xcrun notarytool submit "$DMG" "${AUTH[@]}" --wait | tee "$SUBMIT_LOG"
SUBMIT_STATUS=${PIPESTATUS[0]}
set -e

SUBMISSION_ID="$(grep -E 'id:[[:space:]]+' "$SUBMIT_LOG" | head -n 1 | awk '{ print $2 }' || true)"
if [[ -n "$SUBMISSION_ID" ]]; then
  echo "Submission ID: $SUBMISSION_ID"
  echo "Retrieve logs with:"
  if [[ -n "${APPLE_NOTARY_PROFILE:-}" ]]; then
    echo "  xcrun notarytool log $SUBMISSION_ID --keychain-profile \"$APPLE_NOTARY_PROFILE\""
  else
    echo "  xcrun notarytool log $SUBMISSION_ID --key \"$APPLE_API_KEY\" --key-id \"$APPLE_API_KEY_ID\" --issuer \"$APPLE_API_ISSUER\""
  fi
fi

if [[ $SUBMIT_STATUS -ne 0 ]]; then
  echo "notarytool submit failed with exit $SUBMIT_STATUS" >&2
  exit "$SUBMIT_STATUS"
fi

if ! grep -Eq 'status:[[:space:]]+Accepted' "$SUBMIT_LOG"; then
  echo "notarytool did not report Accepted. See $SUBMIT_LOG" >&2
  exit 1
fi

echo "Stapling $DMG"
xcrun stapler staple "$DMG"
xcrun stapler validate "$DMG"

if [[ -d "$APP_DIR" ]]; then
  echo "Stapling $APP_DIR"
  xcrun stapler staple "$APP_DIR" || true
fi

echo "Notarization complete for $DMG"
