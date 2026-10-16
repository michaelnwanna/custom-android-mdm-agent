#!/bin/sh
set -eu

missing=0
sdk=${ANDROID_HOME:-${ANDROID_SDK_ROOT:-"$HOME/Library/Android/sdk"}}
repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)

check_command() {
    if command -v "$1" >/dev/null 2>&1; then
        printf 'OK      %s\n' "$1"
    else
        printf 'MISSING %s\n' "$1"
        missing=$((missing + 1))
    fi
}

check_file() {
    if [ -f "$1" ]; then
        printf 'OK      %s\n' "$1"
    else
        printf 'MISSING %s\n' "$1"
        missing=$((missing + 1))
    fi
}

printf 'Android feasibility prerequisites\nSDK: %s\n' "$sdk"
if java -version >/dev/null 2>&1; then
    java -version 2>&1
else
    printf 'MISSING working Java installation\n'
    missing=$((missing + 1))
fi
if [ -x "$repo_root/android/gradlew" ] &&
    [ -f "$repo_root/android/gradle/wrapper/gradle-wrapper.jar" ] &&
    [ -f "$repo_root/android/gradle/wrapper/gradle-wrapper.properties" ]; then
    printf 'OK      Gradle wrapper (android/gradlew)\n'
else
    check_command gradle
fi
check_file "$sdk/platform-tools/adb"
check_file "$sdk/emulator/emulator"
check_file "$sdk/platforms/android-34/android.jar"
check_file "$sdk/build-tools/34.0.0/apksigner"

if [ "$missing" -gt 0 ]; then
    printf '\nBLOCKED: %s Android prerequisites missing. See docs/android-feasibility.md.\n' "$missing"
    exit 1
fi
printf '\nPrerequisites found. Check Gradle version and create an API 34 AVD before building.\n'