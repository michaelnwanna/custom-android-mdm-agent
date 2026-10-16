# Recorded Android Tests

All runtime checks added to the lab should run through `tools/android_evidence.py` and retain their evidence. Use a disposable emulator, never a personal device. Python 3, the existing Android SDK/ADB, the Gradle wrapper, and an already-provisioned Fleet Lab Agent Device Owner are required. The runner never wipes an AVD or provisions Device Owner.

## Saved Runs

Latest fresh-source regression: [sixteen update checkpoints passed](../artifacts/android/20261016T094840.126613Z-update/replay.html), with twenty-three unit tests, build/lint for both modules, regenerated negative fixtures, retained APK hashes, and native video validation. This run tests the new persisted kiosk controller; the earlier prepared-artifact result below tests the earlier agent.

Boot milestone evidence: [automatic enabled restoration after cold start](../artifacts/android/20261016T094224.879952Z-cold-boot-recovery/replay.html), [seven-checkpoint controller regression](../artifacts/android/20261016T094556.261617Z-smoke/replay.html), and [disabled intent retained after another cold start](../artifacts/android/20261016T094752.508212Z-disabled-after-cold-start/replay.html) passed with screenshots and natively validated recordings. The [warm-reboot failure](../artifacts/android/20261016T093407.811439Z-reboot/replay.html) remains failed: Android never completed boot within six minutes, so it never reached the restoration assertions. [Framework startup diagnostics](../artifacts/android/20261016T094103.237106Z-boot-diagnostics/manifest.json) and [native service abort logs](../artifacts/android/20261016T094122.804006Z-boot-framework/manifest.json) were captured separately. A [post-boot exit attempt](../artifacts/android/20261016T094325.415344Z-recover/replay.html) correctly cleared lock task but failed the runner's agent-foreground assumption; it is preserved, and focused tests now cover the launcher-return path.

Latest complete result: [sixteen runtime checkpoints passed](../artifacts/android/20261016T092323.365537Z-update/replay.html), including wrong-signer and invalid-APK rejection, silent installation, rendered v2 in kiosk mode, and exit. Fourteen unit tests passed in the same run. Both MP4s passed [native frame validation](../artifacts/android/20261016T092323.365537Z-update/media-validation.json). This was a `--prepared-run` invocation: it reused hash-verified retained APKs and did not rerun build/lint or signing. Its [source record](../artifacts/android/20261016T092323.365537Z-update/prepared-source.json) points to the earlier bundle containing those logs.

The original manual Device Owner/provisioning run was not recorded. Its screenshots cannot be recovered retrospectively. These are clearly labeled new recordings of the runtime checks on the same authorized API 34 lab emulator. Provisioning itself is documented in the earlier [verification record](android-runtime-verification.md), not replayed by this runner.

| Run | Result | Replay |
| --- | --- | --- |
| Initial recorded kiosk rerun after screenshot timing correction | Seven checkpoints passed | [Watch kiosk checks](../artifacts/android/20261016T070600.915765Z-smoke/replay.html) |
| First update attempt | Installation passed; post-update kiosk relaunch failed | [Watch preserved failure](../artifacts/android/20261016T071000.723068Z-update/replay.html) |
| Recovery from failed attempt | Two checkpoints passed | [Watch recovery](../artifacts/android/20261016T071504.159513Z-recover/replay.html) |
| Full update rerun with task relaunch fix | Fourteen checkpoints passed, including kiosk regression and negative APK checks | [Watch passing update suite](../artifacts/android/20261016T071546.926749Z-update/replay.html) |
| Later smoke robustness check | UI Automator timed out; retained as failed | [Inspect timeout](../artifacts/android/20261016T073635.782373Z-smoke/replay.html) |
| Later update setup check | ADB agent replacement timed out before runtime scenarios | [Inspect setup failure](../artifacts/android/20261016T074354.520076Z-update/replay.html) |
| Later v2 display check | Silent install passed; immediate rendered-text assertion failed during slow activity display | [Inspect display failure](../artifacts/android/20261016T074927.909340Z-update/replay.html) |
| Recorded recovery before final rerun | Two checkpoints passed | [Watch recovery](../artifacts/android/20261016T080125.112703Z-recover/replay.html) |
| Final runtime rerun | Eight checkpoints passed, then UI Automator exited 137 and recording retrieval failed with emulator I/O error | [Inspect partial capture](../artifacts/android/20261016T080241.124741Z-update/replay.html) |
| Post-cold-restart health check | Boot, shared storage, Device Owner presence, UI dump, and screenshot capture passed without wiping data | [Inspect health capture](../artifacts/android/20261016T082209.644549Z-health/replay.html) |
| Post-restart recovery | Kiosk already exited; captured current state | [Watch recovery](../artifacts/android/20261016T082242.261296Z-recover/replay.html) |
| First expanded update attempt | Failed before kiosk tap because a System UI ANR dialog obscured controls | [Inspect system dialog](../artifacts/android/20261016T082318.645502Z-update/replay.html) |
| System UI recovery | Recorded ANR dialog dismissal and restoration of agent controls; video validated | [Watch system recovery](../artifacts/android/20261016T083012.131150Z-system-ui-recovery/replay.html) |
| Post-system-recovery kiosk regression | Seven checkpoints and native recording validation passed | [Watch latest kiosk pass](../artifacts/android/20261016T083328.150058Z-smoke/replay.html) |
| Second expanded update attempt | Builds, lint, fixture signing, and baseline installs passed; UI Automator exited 137 before kiosk tap, with a process-system ANR captured | [Inspect framework failure](../artifacts/android/20261016T083818.578832Z-update/replay.html) |
| Prepared artifact validation | All five retained fixture hashes matched and artifacts were copied with source provenance | [Inspect reuse validation](../artifacts/android/20261016T090342.779317Z-prepared-validation/manifest.json) |
| Latest preflight | Fourteen unit tests passed; screen capture preserved an existing process-system ANR, not a healthy runtime gate | [Inspect preflight](../artifacts/android/20261016T090442.194651Z-preflight/replay.html) |
| Explicit framework Wait recovery | Recorded one Wait response; agent controls did not return within the bounded wait, so the chained update suite did not start | [Watch blocked recovery](../artifacts/android/20261016T090545.979055Z-framework-wait/replay.html) |
| Reduced-load cold-boot readiness | Boot and shared storage checks plus capture succeeded after Docker quit; not proof of ANR-free operation | [Inspect readiness](../artifacts/android/20261016T092157.142888Z-reduced-load-readiness/replay.html) |
| First prepared runtime attempt after restart | System UI boot ANR obscured controls before kiosk tap | [Inspect preserved boot failure](../artifacts/android/20261016T092202.776797Z-update/replay.html) |
| Explicit boot ANR recovery | Recorded closing the observed System UI dialog, restored controls, and validated recording | [Watch boot recovery](../artifacts/android/20261016T092257.052605Z-boot-anr-recovery/replay.html) |
| Complete expanded prepared runtime suite | Sixteen checkpoints, fourteen unit tests, and both native video validations passed | [Watch latest update pass](../artifacts/android/20261016T092323.365537Z-update/replay.html) |

The storage-failure rerun is not a passing verification of the latest runner. A subsequent read-only diagnostic found `/sdcard` reporting `Transport endpoint is not connected`, while `/data` had approximately 9.3 GB available. A non-wiping cold restart of the verified `Pixel_8` AVD restored shared storage and capture. A subsequent System UI ANR was preserved and explicitly dismissed in a separate recorded recovery before the seven-checkpoint smoke pass. These recoveries do not establish that the emulator will remain stable indefinitely. Failed runs may leave kiosk mode locked and require explicit recovery once the emulator is healthy.

The runner now waits for the parsed UI text `Version 0.2.0 (2)` instead of asserting immediately after the activity becomes resumed. [Fourteen focused unit tests pass](../artifacts/android/20261016T090442.194651Z-preflight/command-001.log), including delayed rendering, wrong-version rejection, rejection of text present only in unrelated accessibility attributes, fail-closed handling of identical or missing fixture signer certificates, and prepared-artifact reuse with altered/missing hash rejection. The latest sixteen-checkpoint pass also exercised the rendered-text wait and both new on-device rejection cases. Earlier blocked attempts remain preserved rather than relabeled as passes.

After the second expanded attempt, host diagnostics showed approximately 4.5 GiB occupied by the memory compressor and substantial cumulative swapping; a separate virtualization process used approximately 2.9 GB resident memory and 138% CPU. The guest itself reported approximately 1.5 GB available memory. Host contention is a possible contributor, not an established root cause. Do not dismiss framework ANRs automatically or convert these failures to passes; reduce competing host workloads with operator approval before retrying. No unrelated VM was stopped.

During the later continuation, the user explicitly approved interrupting Docker Desktop. Its CLI stop was followed by an observed VM restart; a normal macOS quit then removed that competing process before the emulator booted. The emulator still experienced a System UI keyguard-service ANR, disproving a Docker-only explanation. One separate recorded recovery closed that specific dialog before the passing suite; the runner does not automatically dismiss ANRs. Docker Desktop reported running at restoration time. These observations do not establish long-term emulator stability or a controlled performance comparison.

Open a replay page directly in a local browser; no server or emulator is required to watch saved evidence. Videos have native playback/seek controls; screenshots and platform/UI dumps appear below them. Keep the entire timestamped directory together so relative media links remain valid. A successful capture records observed behavior, not a guarantee of deterministic timing on every host.

The passing update recording was checked with macOS AVFoundation: it has a playable video track and approximately 165 seconds of content. The VS Code integrated browser rejected local MP4 playback despite loading all fourteen screenshots. Use a system browser with H.264 support or open the MP4 directly in QuickTime if the embedded player is unavailable. The screenshot gallery and platform transcript do not depend on video codec support.

Both videos in the passing suite also passed native beginning/midpoint frame decoding at 540x1200; the [saved media validation report](../artifacts/android/20261016T071546.926749Z-update/media-validation.json) records durations and results. Future runner invocations perform this validation automatically on macOS when Swift is installed, and fail the run if it fails. Other hosts retain recordings without this additional native validation. To revalidate a bundle on macOS:

```sh
RECORDING_DIRECTORY="$PWD/artifacts/android/20261016T071546.926749Z-update" \
	swift -suppress-warnings tools/verify-recordings.swift
```

## Rerun Checks

Run these commands from the repository root, using the actual explicitly selected emulator serial:

```sh
python3 tools/android_evidence.py --serial emulator-5554
```

This reruns the parser tests and seven kiosk checkpoints: Device Owner ready, sample locked, Home contained, Recents contained, management navigation, kiosk exit, and normal Home restoration. It inspects already-installed apps and changes kiosk state; it does not rebuild or reinstall them. It expects the default AOSP launcher used in this lab and requires kiosk mode to be exited initially.

```sh
python3 tools/android_evidence.py --serial emulator-5554 --suite update --reset-sample
```

The update suite records the parser tests and Gradle build/lint output, retains agent/v1/v2 and negative-fixture APKs with SHA-256 hashes, installs the updated agent and baseline sample through ADB, reruns the kiosk regression, and then performs the v1-to-v2 update through the agent's public `PackageInstaller` session. ADB is only used to stage v2 into the debug agent's private storage, not to install v2. The expanded suite has sixteen runtime checkpoints; earlier complete runs had fourteen.

Negative fixture generation additionally requires JDK `keytool` on PATH and SDK build-tools 34.0.0 `apksigner`. A temporary RSA lab key signs a copy of the v2 APK; both APK signatures are verified and their certificate digests must differ. The temporary private keystore is deleted, and its fixed test-only password is not a production credential. The invalid fixture contains non-APK bytes. [Saved fixture validation](../artifacts/android/20261016T082055.176402Z-fixtures/manifest.json) includes the signing/verification output and nine passing unit tests; it is not evidence of on-device rejection by itself.

For a lower-load runtime retry, explicitly reuse a prior local bundle that contains all five fixtures and its hash manifest:

```sh
python3 tools/android_evidence.py --serial emulator-5554 --suite update --reset-sample \
	--prepared-run artifacts/android/20261016T083818.578832Z-update
```

This option reruns unit tests, copies and verifies each fixture against the source `apk-sha256.json`, and executes the same sixteen runtime checkpoints. It skips Gradle build/lint and fixture signing; `prepared-source.json` identifies the original bundle and explicitly records `buildAndLintRerun: false`. Refer to that source bundle for build, lint, and signer-verification logs. Do not present this mode as testing newly edited Kotlin sources. Hashes check local artifact consistency, not authenticated release provenance. Default runs still rebuild and lint. Missing artifacts or mismatched/missing hashes fail before installation. This mode completed the latest sixteen-checkpoint pass after an explicit recorded recovery.

`--reset-sample` explicitly permits uninstalling only `dev.fleet.sample` when its installed version is newer than v1, clearing that sample app's data so the transition can be repeated. It does not uninstall the Device Owner agent or wipe the emulator. Omit this flag on the initial v1 baseline if sample data must be preserved; the runner refuses an already-updated baseline instead of silently downgrading it.

The negative cases stage a same-version APK, a wrong-package APK, a separately signed v2 APK, and an invalid APK; each must produce its expected rejection without changing installed version 1. The two new cases also require kiosk lock to remain active. The positive case requires an installer success callback, observed version 2, no pending-user-action callback, the visible `Version 0.2.0 (2)` screen, genuine kiosk lock, and successful exit. A fully passing suite leaves sample v2 installed, the agent as Device Owner, and kiosk mode exited.

```sh
python3 tools/android_evidence.py --serial emulator-5554 --suite recover
```

Recovery records the current known lab screen and exits through its visible controls. It refuses unexpected locked activities. Failures are preserved; normal failed runs attempt a final screenshot/UI/platform capture and video finalization, but do not automatically wipe, reprovision, or hide the failing state. Use recovery explicitly before another suite if a failure left the lab locked.

## Reboot Recovery

```sh
python3 tools/android_evidence.py --serial emulator-5554 --suite reboot
```

This suite requires an already-provisioned emulator with the sample installed and kiosk exited. It builds and lints the current agent, retains its exact APK and hash, runs twenty-three unit tests, and replaces only the agent through ADB. It enables kiosk through visible controls, performs a real non-wiping reboot, and requires automatic sample restoration in genuine `LOCKED` mode without an ADB activity launch. It then exits through the lab controls, reboots again, and requires the ordinary launcher in `NONE` mode. Each reboot must change the Linux boot ID; the receiver's stored result must match Android's current boot count. The suite has six intended runtime checkpoints, but its first run failed during Android framework startup after the first checkpoint. Enabled and disabled behavior passed in the separately linked cold-start observations, not a complete warm-reboot suite. The [disabled baseline](../artifacts/android/20261016T094703.604925Z-disabled-before-cold-start/manifest.json) retains the prior boot ID and preference assertion for comparison with the disabled post-boot run.

Kiosk intent is stored synchronously in credential-protected preferences before starting or exiting. Only `BOOT_COMPLETED` triggers restoration; this is not Direct Boot support and does not promise kiosk before the first user unlock on credential-locked devices. The receiver is non-exported, checks the broadcast action, and uses public Device Owner policy APIs. A failed manual start clears intent and the allowlist; a failed boot attempt retains enabled intent and records a failure for diagnosis, without a retry loop. A successful launch request alone is not runtime proof; the runner separately checks foreground package and platform lock state.

Recordings are split into `before-enabled-reboot.mp4`, `after-enabled-reboot.mp4`, and `after-disabled-reboot.mp4`. Android screen recording cannot span a reboot: the offline boot gaps are represented by timestamped ADB command logs and changing boot IDs, not continuous video. Reboot preference snapshots are saved as `boot-policy-*.xml`. Existing evidence is never overwritten, and framework ANRs are not automatically dismissed.

## Bundle Contents

Each invocation creates a unique UTC-stamped directory under `artifacts/android/`; no earlier run is overwritten:

- `replay.html`: offline gallery and MP4 players.
- PNG screenshots, UI XML, activity state, and Device Policy Manager state at each checkpoint.
- `smoke.mp4`, `update.mp4`, or `recovery.mp4`: screen recordings of the corresponding runtime segment. Earlier smoke runs used `recording.mp4`.
- `manifest.json`: overall result, failure reason, checkpoint timestamps, and command transcript. Binary command output is represented by its SHA-256, not decoded as text.
- `command-*.log`: individual command output, including unit/build/lint output in current runs.
- `environment.txt`: AVD identity and Android fingerprint.
- Update suites additionally retain exact APKs, `apk-sha256.json`, and callback/status preference snapshots.

The current API 34 emulator's `screenrecord` supports `--time-limit 0`, which the runner now uses to avoid truncating slow runtime scenarios. Earlier recordings used a 180-second limit; the verified passing update suite's segments are below it. Recordings cover runtime segments, not the Gradle build screen. Build and unit checks are retained as text logs. Abnormal host termination, lost ADB, or capture-tool failure can leave partial evidence and must not be represented as a complete passing run.

These bundles are local saved files, not uploaded, committed, backed up, signed, or tamper-proof. They may include absolute host paths and device state; review them before sharing. Preserve or archive the full directories for portfolio evidence. Future tests for restrictions, reboot, and MQTT need their own assertions and recorded scenarios; they are not covered simply because the runner exists.

## Installer Scope

The installer is a debug lab probe, not the signed-release delivery system. It accepts only `dev.fleet.sample` version 1 to 2, with matching current APK signer sets, compatible minimum SDK, and a bounded single staged APK. SHA-256 is recorded for diagnostics, not checked against authenticated release metadata. The sample is debug-signed but no longer has `android:testOnly`, because that flag blocks normal public `PackageInstaller` sessions. The DPC remains test-only.

No remote URLs or exported installer trigger are added. Staging uses `run-as` on the debuggable agent, and an operator presses the explicit lab install button. The explicit nonexported result receiver records pending-user-action as a failed silent-install gate. The installed version is checked independently; the runner also launches v2 and checks its rendered version.

Session ID and result diagnostics use lab preferences, not a durable reconciler. Process death before callback, stale busy state, callback loss, partial-session cleanup after reboot, and current-stable Android compatibility still need tests and implementation work. Wrong-signer and invalid-APK cases are now implemented in the runner; consult each run's manifest for whether they actually executed and passed. The invalid fixture does not cover every kind of corruption in an otherwise parseable APK. Signed release manifests, authenticated download/resume, production trust, and cooperative health acknowledgement remain planned. Rendering a version string is not a production application-health protocol.