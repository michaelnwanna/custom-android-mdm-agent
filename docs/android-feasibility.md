# Android Feasibility Lab

Status: both debug APKs compile and pass Android lint. Device Owner provisioning, genuine lock task mode, Home/Recents containment, and visible lab recovery passed on the API 34 emulator. See the [runtime verification record](android-runtime-verification.md) for the tested configuration and limits.

This deliberately recoverable lab tests Device Owner, kiosk, selected settings restrictions, signed maintenance, local v1-to-v2 installation, and MQTT TLS diagnostics before the management backend. Phase 1 is [accepted for API 34 development feasibility](phase-one-acceptance.md); current-stable compatibility is deferred to hardening, not passed. Lab management is visible, but no unauthenticated UI exit remains: exit requires a per-device signed challenge. Debug/ADB provisioning is trusted lab only, not production maintenance or lockdown. The agent is test-only; the sample is debug-signed but not test-only so public installer sessions can update it. No app-initiated destructive management actions are applied. Screenshots, videos, logs, and repeatable commands are in the [recorded test guide](test-evidence.md).

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

The user explicitly authorized wiping the existing `Pixel_8` AVD for the earlier API 34 lab. It was reset and used for the API 34 verification recorded below; this authorization is not permission to wipe other AVDs or physical devices. SDK Command-line Tools 23 are now installed, with checksum verification, on the macOS Intel host. The new Android 17 attempt used a separate `Fleet_Phase1_API37` AVD; `Pixel_8` was shut down intact and no device was wiped during this attempt.

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

Replace the example serial with the actual lab emulator. The `-t` flag permits test-only APK installation. Before entering kiosk, pair the operator through the [automated maintenance procedure](test-evidence.md#maintenance-and-mqtt-diagnostics) while state is `NONE`. Select Start sample kiosk and confirm genuine lock task mode, not screen pinning. Test Home/Recents. Recovery requires Lab management, Request maintenance challenge, a valid operator signature, then Exit lab kiosk.

`LabKioskController` applies `DISALLOW_CONFIG_WIFI`, `DISALLOW_CONFIG_MOBILE_NETWORKS`, and `DISALLOW_APPS_CONTROL`, tracks owned restrictions in credential-protected preferences, and clears only owned restrictions after exit. `LabMaintenance`/`MaintenanceProof` require a per-device P-256 ECDSA signed nonce challenge, with a 120-second monotonic lifetime and boot-count binding; the one-use challenge is consumed before verification, including failed verification.

The runner generates or preserves ignored `.lab-private/<serial>/operator.pem`; its DER public key is paired through debug ADB `run-as` only while `NONE`. `pair_operator` refuses an existing different key. Preserve the matching key securely; key loss requires authorized trusted-emulator ADB reprovisioning, not a UI bypass or automatic key replacement. Private keys and the ignored virtual environment must not be shared. Production maintenance enrollment, rotation, and recovery remain planned.

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

## Current-Stable Compatibility Blocker

Official stable Android 17 / API 37.0 Google APIs x86_64 revision 6 (extension 22) was installed and booted on `Fleet_Phase1_API37`, serial `emulator-5556`, emulator 37.2.12. Catalog schemas 2-4 confirmed channel 0/non-preview; API 37.2 16 KB also exists, without establishing exhaustive latest-minor coverage. The [readiness manifest](../artifacts/android/20261016T112103.738488Z-api37-readiness/manifest.json) passed boot/framework readiness and retained a [screenshot](../artifacts/android/20261016T112103.738488Z-api37-readiness/01-api37-framework-ready.png), but shared-storage/UI-dump errors prevent treating this as fully healthy capture infrastructure.

Agent and sample APK installation succeeded, but standard Device Owner provisioning was rejected for existing accounts despite `dumpsys account` reporting `Accounts: 0`. [Initial provisioning](../artifacts/android/20261016T112535.464679Z-api37-provisioning/manifest.json), [retry](../artifacts/android/20261016T112839.768880Z-api37-provisioning-retry/manifest.json), and [one non-wiping cold retry](../artifacts/android/20261016T113125.637321Z-api37-cold-retry/manifest.json) remain failed. The initial FUSE recording failure retained no MP4; the retry MP4 [failed native validation](../artifacts/android/20261016T112839.768880Z-api37-provisioning-retry/media-validation.json) with no playable video track. See [exact configuration, standard retry command, and limits](test-evidence.md#android-17-current-stable-compatibility-attempt). The cause is unproven; cached account state is only a hypothesis from public-source inspection. No root, hidden API, account/setup-check bypass, or device wipe was used.

Later, [standard provisioning succeeded](../artifacts/android/20261016T120217.950135Z-api37-platform-diagnostics/manifest.json) and [Device Owner state was confirmed](../artifacts/android/20261016T120617.954551Z-api37-system-ui-recovery/manifest.json). The [maintenance retry](../artifacts/android/20261016T120711.924506Z-phase-one/replay.html) passed restriction application, unsigned exit denial, and tampered-signature denial, then failed with UI dump exit 137 and screenrecord finalization timeout. System UI ANR recovery was explicitly recorded before that retry. Framework services subsequently became unavailable; signed recovery failed. The AVD was stopped without wiping, with authenticated exit unconfirmed and potentially enabled kiosk intent. Preserve its paired operator key and verify framework health before signed recovery on the next boot.

The current-stable hardening gate is deferred and does not block Phase 2 under the [revised development acceptance](phase-one-acceptance.md). Complete Android 17 maintenance, kiosk/reboot, update, MQTT, and validated-video coverage remain unverified and are required before broader support or production-readiness claims. The API 34 baseline, thirty-one Python/eight JVM passes, and historical runtime evidence are unchanged.

## Sample Release Versions

Subsequent [controlled recovery experiments](test-evidence.md#controlled-recovery-experiments) retained ownership and observed disabled intent with lock task `NONE`. Four CPUs did not eliminate software-rendered launcher/System UI ANRs; the host GPU backend exposed GLES 3.0 against the emulator's API 37 GLES 3.1 requirement and was not used for acceptance suites. Both emulators are stopped without wiping. These observations supersede the earlier unknown intent state, not the failed signed-recovery result. A stable supported current-stable runtime is still needed.

```sh
./gradlew :sample-managed-app:assembleDebug -PsampleVersionCode=2
```

This produces a visibly versioned v2 APK, using the same local debug signer if its keystore is unchanged. Building alone does not install the update. The [recorded update suite](test-evidence.md) now stages this APK in the debug agent's private storage and exercises its lab-only PackageInstaller flow. It passed on API 34: callback success, observed version code 2, no pending-user-action callback, rendered v2 in lock task mode, and recovery. Same-version and wrong-package inputs were rejected. ADB baseline installation is not evidence of agent-driven installation; the v2 transition itself uses the agent. Production release-signing, manifest trust, resumable delivery, session reconciliation, and health acknowledgement are separate later work.

## Remaining Feasibility Gates

| Gate | Current status |
| --- | --- |
| Compile and lint both APKs | Passed on the toolchain recorded above |
| Device Owner provisioning | Passed on reset Pixel_8, API 34, target SDK 34 |
| Current-stable compatibility | API 37.0 Device Owner confirmed after earlier rejections; three restriction/maintenance assertions passed in an overall failed run. Framework loss blocked complete maintenance, kiosk/reboot, update, MQTT, and validated-video coverage |
| Actual lock task mode and recovery | Passed: LOCKED, Home/Recents contained; current exit requires signed maintenance |
| Selected settings restrictions | API 34 restriction apply/remove passed; owned-only cleanup implemented |
| Agent-driven silent v1-to-v2 installation | Passed on API 34 with locally staged debug APK; not production release delivery |
| Kiosk restoration after reboot | Enabled/disabled cold boots passed; [six warm-reboot checkpoints](../artifacts/android/20261016T104301.613674Z-reboot/replay.html) and [manifest](../artifacts/android/20261016T104301.613674Z-reboot/manifest.json) passed two genuine warm reboots: automatic enabled restoration/Home containment, then disabled no-restoration/persisted exit. The earlier framework-startup failure remains failed |
| MQTT lifecycle while sample app is foreground | [Seven lifecycle checkpoints](../artifacts/android/20261016T104119.390614Z-mqtt-lifecycle/replay.html) and [manifest](../artifacts/android/20261016T104119.390614Z-mqtt-lifecycle/manifest.json) passed TLS roundtrip, foreground receive/ack, broker-offline containment, fresh ACK after reconnect, process-death containment/no immortal socket, explicit restart, and explicit stop |
| Authenticated maintenance access | Five checkpoints passed: restrictions, unsigned/tampered denial, signed exit/cleanup, consumed-challenge denial; debug/ADB pairing is not production maintenance |

The [fresh sixteen-checkpoint update replay](../artifacts/android/20261016T104623.288748Z-update/replay.html) and [manifest](../artifacts/android/20261016T104623.288748Z-update/manifest.json) passed build/lint invocations, four APK rejection cases, silent v2 installation/relaunch, restrictions, and signed maintenance recovery, with twenty-seven then-current Python tests. The [latest five-checkpoint maintenance replay](../artifacts/android/20261016T110746.710228Z-phase-one/replay.html) and [manifest](../artifacts/android/20261016T110746.710228Z-phase-one/manifest.json) passed restriction application, unsigned/tampered/consumed-challenge rejection, signed cleanup, and build/lint. Both runs, plus the warm-reboot and MQTT lifecycle bundles above, passed native video validation. Current totals are eight JVM MaintenanceProof tests and thirty-one Python tests, including four new operator-pairing regression tests.

MQTT uses HiveMQ 1.3.3 in a test-only, user-started, nonexported `dataSync` foreground service targeting SDK 34, with a notification, `START_NOT_STICKY`, and no boot autostart. TLS uses `localhost:8883` via `adb reverse`, staged `.lab-private/mqtt/server.pem` scoped trust, and normal hostname verification. Anonymous synthetic probes on loopback aMQTT TLS are not production command transport; see [setup and limits](test-evidence.md#maintenance-and-mqtt-diagnostics). Current-stable compatibility remains a deferred hardening gate. API 34 emulator passes do not establish physical-device/OEM reliability, Direct Boot support, or production lifecycle guarantees.

Do not move to backend expansion on the strength of source inspection alone. Record emulator/API/target versions, actual results, failure reasons, and recovery behavior as these gates are tested.