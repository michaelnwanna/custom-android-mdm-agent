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

## Subsequent Boot Verification

An earlier continuation completed a [sixteen-checkpoint expanded runtime pass](../artifacts/android/20261016T092323.365537Z-update/replay.html). It ran fourteen unit tests, verified all five retained fixture hashes, rejected wrong-signer and invalid APKs without changing v1, installed v2 silently, rendered v2 under genuine kiosk lock, and exited successfully. Both recordings passed native frame decoding. This was a prepared-artifact run, not a fresh build/lint invocation; the source bundle retains those earlier results. An explicitly recorded System UI boot-ANR recovery preceded the pass. Sample v2 was installed and kiosk exited at completion; repeat runs can change that state. Earlier failed attempts remain linked in the [evidence guide](test-evidence.md).

Persisted kiosk intent and automatic post-unlock restoration are now implemented through a shared kiosk controller and `BOOT_COMPLETED` receiver. The first automated warm reboot changed boot ID but Android failed to complete boot within six minutes; follow-up diagnostics found no Activity Manager service and a native Android service abort. A separate non-wiping cold start then [restored the sample automatically](../artifacts/android/20261016T094224.879952Z-cold-boot-recovery/replay.html), with receiver diagnostics matching the current boot count and genuine `LOCKED` state. No ADB app launch was used to achieve restoration. After explicit exit, another cold start [retained disabled intent and an unlocked launcher](../artifacts/android/20261016T094752.508212Z-disabled-after-cold-start/replay.html) before opening diagnostics. A recovery attempt also exposed a runner assumption: post-boot exit may return to the launcher, not the agent. The runner now independently checks `NONE` before reopening the agent and rejects unknown foreground packages. Twenty-three unit tests and a [fresh-source sixteen-checkpoint update regression](../artifacts/android/20261016T094840.126613Z-update/replay.html) passed. All three passing runtime bundles have native video validation. See the [reboot procedure and limits](test-evidence.md#reboot-recovery).

## Restrictions, Maintenance, and MQTT Continuation

Later API 34 evidence adds [signed recovery](../artifacts/android/20261016T101848.772175Z-recover/replay.html) and a [seven-checkpoint restriction smoke pass](../artifacts/android/20261016T101959.563123Z-smoke/replay.html). `LabKioskController` applies `DISALLOW_CONFIG_WIFI`, `DISALLOW_CONFIG_MOBILE_NETWORKS`, and `DISALLOW_APPS_CONTROL`, persists owned restrictions in credential-protected preferences, and clears only owned restrictions after exit.

The [latest five-checkpoint maintenance run](../artifacts/android/20261016T110746.710228Z-phase-one/replay.html) and [manifest](../artifacts/android/20261016T110746.710228Z-phase-one/manifest.json) passed restriction application, unsigned exit denial, tampered signature denial, signed exit/cleanup, and consumed-challenge denial, plus build/lint and native video validation. Eight JVM MaintenanceProof tests and thirty-one Python tests passed, including four new operator-pairing regression tests. `LabMaintenance`/`MaintenanceProof` require a per-device P-256 ECDSA signed nonce challenge with a 120-second monotonic lifetime, boot-count binding, and one-use consumption before verification. The UI requires Request maintenance challenge, then Exit lab kiosk after a valid response. No unauthenticated UI exit remains; the initial manual exit table above describes older behavior.

The runner generates/preserves ignored `.lab-private/<serial>/operator.pem`; its DER public key is paired using debug ADB `run-as` only while `NONE`, and `pair_operator` refuses an existing different key. `EvidenceRun.maintenance_exit` now serves smoke/update/recover/reboot. Debug/ADB provisioning is trusted lab only, not production maintenance. Key loss requires authorized trusted-emulator ADB reprovisioning, not a bypass. Never publish private keys or virtual-environment contents.

The earlier [three-checkpoint MQTT run](../artifacts/android/20261016T103427.695116Z-mqtt/replay.html) passed TLS roundtrip, receive/ack while the sample was foreground and `LOCKED`, and signed exit with explicit stop; service dump `isForeground=true` and native video validation passed. The later [seven-checkpoint lifecycle replay](../artifacts/android/20261016T104119.390614Z-mqtt-lifecycle/replay.html) and [manifest](../artifacts/android/20261016T104119.390614Z-mqtt-lifecycle/manifest.json) extend this to broker-offline containment, reconnect with a fresh probe ACK, process-death containment without an immortal socket, explicit probe restart, and explicit stop. Its native video validation passed. HiveMQ MQTT Client 1.3.3 uses a test-only, user-started, nonexported `dataSync` foreground service targeting SDK 34, with a notification, `START_NOT_STICKY`, and no boot autostart. Explicit restart after process death is verified, not automatic durable transport recovery. TLS uses `localhost:8883` through ADB reverse, staged `.lab-private/mqtt/server.pem` scoped trust, and normal hostname verification.

Isolated pinned amqtt 0.11.3 serves anonymous synthetic probes ONLY on TLS `127.0.0.1:8883`, with no command handling. This is not the planned production Mosquitto/mTLS/ACL backend infrastructure. The Homebrew Mosquitto attempt was interrupted due to a large toolchain source build; no Docker was used for this MQTT setup. See [repeatable commands and security limits](test-evidence.md#maintenance-and-mqtt-diagnostics).

## Warm-Reboot and Fresh Update Verification

The [six-checkpoint warm-reboot replay](../artifacts/android/20261016T104301.613674Z-reboot/replay.html) and [manifest](../artifacts/android/20261016T104301.613674Z-reboot/manifest.json) passed on API 34. Two genuine non-wiping `adb reboot` operations changed the Linux boot ID. With enabled intent, the sample restored automatically in `LOCKED` state without an ADB activity launch and contained Home. After signed maintenance exit, the second reboot retained disabled intent, left the ordinary launcher in `NONE`, and confirmed persisted exit. Boot-policy snapshots and receiver/current boot-count checks support these results; all three recording segments passed native video validation. The earlier [093407 warm-reboot failure](../artifacts/android/20261016T093407.811439Z-reboot/replay.html) remains a failed framework-startup attempt, not a retroactive pass. Neither cold nor warm passes establish indefinite emulator reliability or pre-unlock restoration.

The [fresh sixteen-checkpoint update replay](../artifacts/android/20261016T104623.288748Z-update/replay.html) and [manifest](../artifacts/android/20261016T104623.288748Z-update/manifest.json) passed twenty-seven then-current Python tests, agent/sample build and lint invocations, regenerated negative fixtures, kiosk regression with restrictions and signed maintenance, four APK rejections, silent local v1-to-v2 installation, visible v2 under lock, and signed recovery. Both recordings passed native video validation. This run does not use prepared artifacts and supersedes the pre-maintenance update coverage, not the earlier failed records. The [latest maintenance manifest](../artifacts/android/20261016T110746.710228Z-phase-one/manifest.json) separately records the five signed-maintenance/restriction checks and the JVM test invocation; the current counts are eight JVM and thirty-one Python tests. The [earlier maintenance manifest](../artifacts/android/20261016T103030.443356Z-phase-one/manifest.json) retains its twenty-six then-current Python tests.

## Android 17 Compatibility Attempt

This is a separate compatibility attempt, not an extension of the API 34 passes. On the macOS Intel host, checksum-verified SDK Command-line Tools 23 installed the official stable Android 17 / API 37.0 Google APIs x86_64 revision 6, extension 22. Catalog schemas 2-4 confirmed channel 0/non-preview. API 37.2 16 KB also exists; this attempt does not establish exhaustive latest-minor coverage. The separate AVD was `Fleet_Phase1_API37`, serial `emulator-5556`, running emulator 37.2.12. Its fingerprint was `google/sdk_gphone64_x86_64/emu64xa:17/CE2A.260420.019/15611780:userdebug/dev-keys`. The SDK 34 build/target was unchanged. Original `Pixel_8` was shut down intact; no device was wiped in this attempt.

| Attempt | Observed result |
| --- | --- |
| [112103 readiness manifest](../artifacts/android/20261016T112103.738488Z-api37-readiness/manifest.json) | Boot/framework readiness passed with a [screen capture](../artifacts/android/20261016T112103.738488Z-api37-readiness/01-api37-framework-ready.png); shared-storage/UI-dump errors remain in the record |
| [112535 provisioning manifest](../artifacts/android/20261016T112535.464679Z-api37-provisioning/manifest.json) | Agent/sample APK installs succeeded; standard `dpm set-device-owner` rejected existing accounts despite `dumpsys account` reporting `Accounts: 0`. FUSE `Transport endpoint is not connected` prevented recording; no MP4 retained |
| [112839 provisioning retry manifest](../artifacts/android/20261016T112839.768880Z-api37-provisioning-retry/manifest.json) | Same provisioning rejection; retained MP4 [failed native media validation](../artifacts/android/20261016T112839.768880Z-api37-provisioning-retry/media-validation.json): no playable video track, not successful video evidence |
| [113125 cold retry manifest](../artifacts/android/20261016T113125.637321Z-api37-cold-retry/manifest.json) | One non-wiping cold restart, same standard provisioning rejection |

The [420-second readiness probe](../artifacts/android/20261016T115351.430818Z-api37-account-readiness/manifest.json) timed out before provisioning. A subsequent [standard provisioning retry succeeded](../artifacts/android/20261016T120217.950135Z-api37-platform-diagnostics/manifest.json); its platform logs retain a KeyChain binding timeout and launcher crash. The [first maintenance run](../artifacts/android/20261016T120352.839329Z-phase-one/replay.html) stopped before kiosk entry because a System UI ANR dialog covered the controls. An [explicit Wait recovery](../artifacts/android/20261016T120617.954551Z-api37-system-ui-recovery/replay.html) captured before/after screenshots and confirmed Device Owner state; it has no video.

The [maintenance retry](../artifacts/android/20261016T120711.924506Z-phase-one/replay.html) passed restriction application, unsigned exit denial, and tampered-signature denial, then failed with UI dump exit 137 and recording finalization timeout. [Signed recovery failed](../artifacts/android/20261016T121330.133823Z-recover/manifest.json); [framework-loss diagnostics](../artifacts/android/20261016T121409.275165Z-api37-framework-loss/manifest.json) retain unavailable Activity Manager. The AVD was stopped without wiping. Authenticated exit is unconfirmed; enabled kiosk intent may restore next boot. The runner's owner-check failure during framework loss does not prove ownership removal.

No root, hidden API, account/setup-check bypass, or device wipe was used. Cached account state remains a hypothesis, not a proven cause. Android 17 Device Owner is confirmed, but complete maintenance, kiosk/reboot, update, MQTT, and validated-video compatibility remain **unverified**. See the [evidence details](test-evidence.md#android-17-current-stable-compatibility-attempt). API 34 runtime evidence and the thirty-one Python/eight JVM passes are unchanged.

## Still Open

Further [controlled recovery experiments](test-evidence.md#controlled-recovery-experiments) did not complete compatibility: four CPUs with software rendering still produced launcher/System UI ANRs, and host GPU rendering exposed GLES 3.0 against the emulator's API 37 GLES 3.1 requirement. The [four-core closeout](../artifacts/android/20261016T123148.859350Z-api37-four-core-closeout/manifest.json) observed retained Device Owner, lock task `NONE`, and persisted kiosk intent `false`, superseding the earlier unknown intent state without claiming successful signed recovery. Both emulators are stopped without wiping. A stable supported current-stable runtime remains necessary; the software-rendering failure's cause is unproven.

Phase 1 is accepted for API 34 development feasibility under the [revised scope decision](phase-one-acceptance.md). Current-stable API 37 compatibility is deferred to hardening, required before broader support or production-readiness claims, and does not block Phase 2. Warm-reboot restoration and the test-only MQTT failure lifecycle are verified on the API 34 emulator. The local installer is a feasibility probe, not authenticated signed-release delivery or durable session recovery. No hardware device, physical navigation gesture, Direct Boot/pre-unlock restoration, production maintenance, or production update workflow was verified. Broader physical-device/OEM reliability and compatibility across all API levels starting at 30 are not established. Earlier failed artifacts remain failures and are preserved in the evidence guide.