#!/usr/bin/env bash
# Runs a Flutter command with Entra dart-defines and the Android signature hash.
# Usage: ENTRA_TENANT_ID=... ENTRA_CLIENT_ID=... ENTRA_API_SCOPE=... API_BASE_URL=... \
#   tool/flutter_entra.sh run|build apk|build ios ... [extra flutter args]
# Optional QR enrollment values: AGENT_APK_URL, AGENT_SIGNATURE_CHECKSUM, SERVER_ROOT_CA, DEVICE_ROOT_CA.
set -euo pipefail

for name in ENTRA_TENANT_ID ENTRA_CLIENT_ID ENTRA_API_SCOPE API_BASE_URL; do
  if [[ -z "${!name:-}" ]]; then
    echo "Missing required environment variable: $name" >&2
    exit 1
  fi
done

if [[ -z "${ENTRA_ANDROID_SIGNATURE_HASH:-}" ]]; then
  keystore="${ANDROID_KEYSTORE:-$HOME/.android/debug.keystore}"
  alias="${ANDROID_KEY_ALIAS:-androiddebugkey}"
  storepass="${ANDROID_KEYSTORE_PASSWORD:-android}"
  if [[ -f "$keystore" ]]; then
    ENTRA_ANDROID_SIGNATURE_HASH="$(keytool -exportcert -alias "$alias" -keystore "$keystore" \
      -storepass "$storepass" 2>/dev/null | openssl sha1 -binary | openssl base64)"
  fi
fi

cd "$(dirname "$0")/.."
args=(
  "--dart-define=ENTRA_TENANT_ID=$ENTRA_TENANT_ID"
  "--dart-define=ENTRA_CLIENT_ID=$ENTRA_CLIENT_ID"
  "--dart-define=ENTRA_API_SCOPE=$ENTRA_API_SCOPE"
  "--dart-define=API_BASE_URL=$API_BASE_URL"
)
for name in AGENT_APK_URL AGENT_SIGNATURE_CHECKSUM SERVER_ROOT_CA DEVICE_ROOT_CA; do
  if [[ -n "${!name:-}" ]]; then
    args+=("--dart-define=$name=${!name}")
  fi
done

android_capable=false
case "${1:-} ${2:-}" in
  "run "* | "build apk" | "build appbundle") android_capable=true ;;
esac
if [[ "$android_capable" == true && -n "${ENTRA_ANDROID_SIGNATURE_HASH:-}" ]]; then
  echo "Android signature hash: $ENTRA_ANDROID_SIGNATURE_HASH" >&2
  args+=(
    "--dart-define=ENTRA_ANDROID_SIGNATURE_HASH=$ENTRA_ANDROID_SIGNATURE_HASH"
    "--android-project-arg=msalSignatureHash=$ENTRA_ANDROID_SIGNATURE_HASH"
  )
fi

exec flutter "$@" "${args[@]}"
