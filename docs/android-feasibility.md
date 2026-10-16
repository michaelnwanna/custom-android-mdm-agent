# Android Feasibility Lab

Status: both debug APKs compile and pass Android lint. Device Owner provisioning, genuine lock task mode, Home/Recents containment, and visible lab recovery passed on the API 34 emulator. See the [runtime verification record](android-runtime-verification.md) for the tested configuration and limits.

This deliberately recoverable lab tests Device Owner provisioning, lock task mode, and a locally staged v1-to-v2 installation before building the management backend. It is not a secured kiosk deployment: anyone can use the visible Lab management button to exit. The agent is test-only; the sample is debug-signed but not flagged test-only so public installer sessions can update it. No network/settings restrictions or app-initiated destructive management actions are applied. Screenshots, videos, logs, and repeatable test commands are documented in the [recorded test guide](test-evidence.md).

## Prerequisites

- JDK 17 or 21; this machine currently has JDK 21.
- The included Gradle 8.9 wrapper; no system Gradle installation is required. Android Gradle Plugin 8.7.3 and Kotlin 2.0.21 are pinned in the build.
- Android SDK platform 34, build tools 34.0.0, platform-tools, and emulator.
- A disposable API 34 Android Virtual Device appropriate for the host CPU, without personal accounts or data.

Install Android Studio from the official Android developer site and use SDK Manager to install the packages above. Set `ANDROID_HOME` to the SDK directory (normally `$HOME/Library/Android/sdk` on macOS) and put its platform-tools/emulator directories on PATH. SDK installation and license acceptance are not performed automatically. The required SDK files are now present on the development machine.

Run from the repository root:

```sh
sh tools/doctor.sh
```

The checker exits nonzero for missing prerequisites. It does not install tools or prove emulator readiness.

## Build

From the repository root:

```sh
export ANDROID_HOME="$HOME/Library/Android/sdk"
cd android
./gradlew --version
./gradlew :agent:assembleDebug :sample-managed-app:assembleDebug :agent:lintDebug :sample-managed-app:lintDebug
```

The wrapper pins the Gradle distribution SHA-256 in its properties. Its bootstrap distribution and wrapper JAR were checked against the official Gradle 8.9 published checksums. Use the wrapper for subsequent builds and retain its scripts, properties, and JAR in version control. Do not commit local SDK paths or signing keys. The first build needs network access to download Gradle and Android build dependencies.

## Build Verification

Verified on 2026-10-16 with macOS 15.7.8 x86_64, Temurin JDK 21.0.8, Gradle 8.9, Android Gradle Plugin 8.7.3, Kotlin 2.0.21, and SDK platform 34:

- `sh -n tools/doctor.sh` and `sh tools/doctor.sh`: passed from the repository root.
- `:agent:assembleDebug` and `:sample-managed-app:assembleDebug`: passed.
- `:agent:lintDebug` and `:sample-managed-app:lintDebug`: passed. This is not a claim of zero lint warnings.
- Gradle distribution SHA-256: `d725d707bfabd4dfdc958c624003b3c80accc03f7037b5122c4b1d0ef15cecab`.
- Gradle wrapper JAR SHA-256: `498495120a03b9a6ab5d155f5de3c8f0d986a449153702fb80fc80e134484f17`.

The build emitted an SDK XML version compatibility warning from the installed SDK tooling; it did not fail compilation or lint. Reports are generated under each module's `build/reports/` directory. Build success does not prove Device Owner provisioning, lock task behavior, or silent installation.

## Disposable Emulator Provisioning

Use a fresh dedicated lab AVD with no accounts. Device Owner provisioning can fail if provisioning prerequisites are not met; do not reset an existing device automatically. Before each command, confirm that the explicit serial identifies the disposable emulator.

The user explicitly authorized wiping the existing `Pixel_8` AVD for this lab. It was reset and used for the API 34 verification recorded below; this authorization is not permission to wipe other AVDs or physical devices. The SDK command-line tools (`avdmanager`) are not installed at the standard SDK location, but the `emulator` executable can launch an existing AVD without them. For a new separate AVD, use Android Studio Device Manager or install Android SDK Command-line Tools through SDK Manager.

On the current macOS Intel host, the windowed software-graphics launch did not complete boot within the observation period. This headless configuration subsequently reached `sys.boot_completed=1` and supported the Device Owner/kiosk tests:

```sh
"$ANDROID_HOME/emulator/emulator" -avd Pixel_8 -no-snapshot -no-window \
	-no-boot-anim -no-audio -gpu swiftshader -feature -Vulkan \
	-camera-back none -camera-front none
```

This command preserves the existing AVD data. Only add `-wipe-data` for a specifically authorized destructive reset, before provisioning; it deletes installed apps and policy state. Confirm `sys.boot_completed` is `1` and the package manager responds before installing APKs. An ADB device marked `offline`, or a running emulator process alone, is not proof of a booted Android system.

From the Android directory:

```sh
adb devices -l
export ANDROID_SERIAL=emulator-5554
adb -s "$ANDROID_SERIAL" install -t agent/build/outputs/apk/debug/agent-debug.apk
adb -s "$ANDROID_SERIAL" install -t sample-managed-app/build/outputs/apk/debug/sample-managed-app-debug.apk
adb -s "$ANDROID_SERIAL" shell dpm set-device-owner dev.fleet.agent/.FleetAdminReceiver
adb -s "$ANDROID_SERIAL" shell am start -n dev.fleet.agent/.MainActivity
```

Replace the example serial with the actual lab emulator. The `-t` flag permits test-only APK installation. Select Start sample kiosk. Confirm that Device Owner is true and lock task mode is locked, not screen pinned. Test Home/Recents and use Lab management followed by Exit lab kiosk to recover.

Independent diagnostic commands:

```sh
adb -s "$ANDROID_SERIAL" shell dumpsys device_policy
adb -s "$ANDROID_SERIAL" shell dumpsys activity activities
```

For lab cleanup, exit kiosk first, then remove the test-only administrator:

```sh
adb -s "$ANDROID_SERIAL" shell dpm remove-active-admin dev.fleet.agent/.FleetAdminReceiver
```

No app-initiated factory reset or wipe capability is implemented. The one authorized host-side AVD reset and manual ADB provisioning are documented lab setup operations, not agent features.

## Sample Release Versions

```sh
./gradlew :sample-managed-app:assembleDebug -PsampleVersionCode=2
```

This produces a visibly versioned v2 APK, using the same local debug signer if its keystore is unchanged. Building alone does not install the update. The [recorded update suite](test-evidence.md) now stages this APK in the debug agent's private storage and exercises its lab-only PackageInstaller flow. It passed on API 34: callback success, observed version code 2, no pending-user-action callback, rendered v2 in lock task mode, and recovery. Same-version and wrong-package inputs were rejected. ADB baseline installation is not evidence of agent-driven installation; the v2 transition itself uses the agent. Production release-signing, manifest trust, resumable delivery, session reconciliation, and health acknowledgement are separate later work.

## Remaining Feasibility Gates

| Gate | Current status |
| --- | --- |
| Compile and lint both APKs | Passed on the toolchain recorded above |
| Device Owner provisioning | Passed on reset Pixel_8, API 34, target SDK 34 |
| Actual lock task mode and recovery | Passed: LOCKED, Home/Recents contained, visible exit clears lock task |
| Selected settings restrictions | Not implemented |
| Agent-driven silent v1-to-v2 installation | Passed on API 34 with locally staged debug APK; not production release delivery |
| Kiosk restoration after reboot | Persisted post-unlock intent implemented; enabled restoration and disabled exit verified across non-wiping cold boots on API 34. Warm-reboot suite blocked by framework startup failure; see [evidence](test-evidence.md#reboot-recovery) |
| MQTT lifecycle while sample app is foreground | Not implemented |
| Authenticated maintenance access | Not implemented; visible debug-only escape instead |

Do not move to backend expansion on the strength of source inspection alone. Record emulator/API/target versions, actual results, failure reasons, and recovery behavior as these gates are tested.