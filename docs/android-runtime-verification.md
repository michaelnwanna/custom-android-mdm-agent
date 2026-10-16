# Android Runtime Verification

Verified: 2026-10-16. Scope: the initial test-only Device Owner and single-app kiosk feasibility lab, not a completed phase 1 or a production lockdown deployment.

## Environment

| Item | Observed value |
| --- | --- |
| Host | macOS 15.7.8, x86_64; Hypervisor.Framework available |
| Emulator | 37.2.12.0, build 16428233 |
| AVD | `Pixel_8`; user explicitly authorized its destructive reset |
| Serial during this run | `emulator-5554` |
| Image | Android 14 / API 34, default x86_64 image |
| Build fingerprint | `Android/sdk_phone64_x86_64/emu64x:14/UE1A.230829.036.A1/11228894:userdebug/test-keys` |
| Agent | `dev.fleet.agent`, version code 1, min SDK 30, target SDK 34 |
| Sample | `dev.fleet.sample`, displayed version `0.1.0 (1)` |
| Signing | Both APKs verified with `apksigner`; same local Android debug certificate |

The windowed software-graphics attempt remained offline or lacked package services during the observation period. It was stopped. A subsequent headless cold boot with Vulkan and cameras disabled reached `sys.boot_completed=1`. The precise cause of the earlier slow/stalled startup was not established. The working command is in the [feasibility guide](android-feasibility.md).

## Executed Checks

| Check | Evidence and result |
| --- | --- |
| Android ready | `getprop sys.boot_completed` returned `1`; package manager responded |
| APK installation | Both `adb -s emulator-5554 install -t ...` operations returned `Success` |
| Device Owner | `dpm set-device-owner dev.fleet.agent/.FleetAdminReceiver` succeeded; `dumpsys device_policy` reported that component as Device Owner for user 0 |
| Agent diagnostics | UI hierarchy reported `Device Owner: true` |
| Kiosk entry | Pressed the rendered `START SAMPLE KIOSK` button; sample app opened |
| Genuine lock task | After the asynchronous transition, `dumpsys activity activities` reported `mLockTaskModeState=LOCKED` and the sample task in `mLockTaskModeTasks` |
| Home containment | Sent `KEYCODE_HOME`; sample remained top-resumed and lock task remained `LOCKED` |
| Recents containment | Sent `KEYCODE_APP_SWITCH`; sample remained top-resumed and lock task remained `LOCKED` |
| Lab management | Pressed `LAB MANAGEMENT`; agent became top-resumed while lock task remained `LOCKED`; its UI reported `Lock task locked: true` |
| Recovery | Pressed `EXIT LAB KIOSK`; platform state became `NONE` and the lock task package allowlist became empty |
| Normal Home restored | Sent `KEYCODE_HOME` after recovery; `com.android.launcher3/.uioverrides.QuickstepLauncher` became top-resumed |

UI controls were located using `uiautomator dump` and exercised using ADB input. An immediate dump during kiosk startup initially showed `NONE`; the later platform state confirmed `LOCKED`. Future automated tests must wait for the expected state with a bounded deadline rather than assume the launch and lock transition are synchronous.

## Recheck Commands

Confirm the serial and AVD identity before any device mutation. These commands inspect the existing lab state and do not wipe or reprovision it:

```sh
adb devices -l
adb -s emulator-5554 emu avd name
adb -s emulator-5554 shell getprop sys.boot_completed
adb -s emulator-5554 shell dumpsys device_policy
adb -s emulator-5554 shell dumpsys activity activities
```

Final tested policy state: the agent remains Device Owner, both lab apps are installed, and kiosk mode has been exited with an empty lock task allowlist. No administrator removal was performed. The original checks above were manual and unrecorded. They have since been rerun using an automated evidence runner, with saved screenshots, MP4 recordings, platform assertions, and command transcripts; see the [recorded runs and rerun guide](test-evidence.md).

## Recorded Update Verification

The passing recorded update suite on 2026-10-16 completed fourteen checkpoints. It rebuilt and linted both modules, ran six runner parser tests, reran the kiosk regression, rejected same-version and wrong-package APKs, and installed sample v2 through the agent's PackageInstaller session without requesting user confirmation. It independently observed version code 2, rendered `Version 0.2.0 (2)` in `LOCKED` mode, and exited kiosk successfully. The sample no longer carries the test-only manifest flag; the DPC remains test-only.

The first recorded attempt exposed a post-update task-reuse defect: the agent remained topmost when the sample launcher reused its old task. That failed run is preserved. Adding `FLAG_ACTIVITY_CLEAR_TOP` to the sample launch intent allowed the passing rerun to render v2. That run ended with sample v2 installed and kiosk mode exited; later attempts reset the sample to v1 for repeat testing, so consult the latest run rather than treating that historical state as current. The run directories, exact APKs and hashes, negative checks, and limits are linked in the [evidence guide](test-evidence.md).

A later non-wiping cold restart restored failed shared storage while retaining Device Owner provisioning. System UI and framework ANRs interrupted two expanded attempts; their screenshots and a separate recorded System UI recovery are preserved. Between them, a seven-checkpoint kiosk regression and native video validation passed. These historical attempts did not reach the new wrong-signer/invalid-APK cases and predated boot restoration. Later results below supersede their incomplete verification, not their preserved failure records.

## Still Open

The latest continuation completed a [sixteen-checkpoint expanded runtime pass](../artifacts/android/20261016T092323.365537Z-update/replay.html). It ran fourteen unit tests, verified all five retained fixture hashes, rejected wrong-signer and invalid APKs without changing v1, installed v2 silently, rendered v2 under genuine kiosk lock, and exited successfully. Both recordings passed native frame decoding. This was a prepared-artifact run, not a fresh build/lint invocation; the source bundle retains those earlier results. An explicitly recorded System UI boot-ANR recovery preceded the pass. Sample v2 was installed and kiosk exited at completion; repeat runs can change that state. Earlier failed attempts remain linked in the [evidence guide](test-evidence.md).

Persisted kiosk intent and automatic post-unlock restoration are now implemented through a shared kiosk controller and `BOOT_COMPLETED` receiver. The first automated warm reboot changed boot ID but Android failed to complete boot within six minutes; follow-up diagnostics found no Activity Manager service and a native Android service abort. A separate non-wiping cold start then [restored the sample automatically](../artifacts/android/20261016T094224.879952Z-cold-boot-recovery/replay.html), with receiver diagnostics matching the current boot count and genuine `LOCKED` state. No ADB app launch was used to achieve restoration. After explicit exit, another cold start [retained disabled intent and an unlocked launcher](../artifacts/android/20261016T094752.508212Z-disabled-after-cold-start/replay.html) before opening diagnostics. A recovery attempt also exposed a runner assumption: post-boot exit may return to the launcher, not the agent. The runner now independently checks `NONE` before reopening the agent and rejects unknown foreground packages. Twenty-three unit tests and a [fresh-source sixteen-checkpoint update regression](../artifacts/android/20261016T094840.126613Z-update/replay.html) passed. All three passing runtime bundles have native video validation. See the [reboot procedure and limits](test-evidence.md#reboot-recovery).

Selected settings restrictions, MQTT lifecycle while the sample is foreground, and authenticated maintenance remain unimplemented. The local installer is a feasibility probe, not authenticated signed-release delivery or durable session recovery. The visible lab exit is intentionally unauthenticated. Cold-boot restoration is verified only on this unlocked API 34 image; the automated warm-reboot suite is not passing. No hardware device, current-stable Android image, physical navigation gesture, Direct Boot/pre-unlock restoration, or production update workflow was verified. These results do not establish compatibility across all API levels starting at 30.