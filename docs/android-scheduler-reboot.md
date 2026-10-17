# Persisted Probe Recovery Across Reboot

The debug-only WorkManager probe now survives an actual non-wiping API 34 emulator reboot and completes after explicit instrumentation relaunch. This extends [force-stop recovery](android-scheduler-recovery.md). It is not automatic execution at boot or real event-delivery recovery.

```mermaid
sequenceDiagram
    participant Host as Trusted lab host
    participant App as Agent instrumentation
    participant Disk as Persisted WorkManager and probe files
    participant Android as API 34 emulator
    Host->>App: Prepare random unique delayed probe
    App->>Disk: Persist work ID and preparation boot count
    Host->>Android: Force-stop agent; require old PID absent
    Host->>Android: Read kernel boot ID; adb reboot
    Android-->>Host: ADB disconnect then reconnect; boot and storage ready
    Host->>Android: Require changed kernel boot ID
    Host->>App: Explicit resume; no enqueue
    App->>Disk: Require increased boot count and original work ID
    Disk->>App: Dispatch attempts 0 and 1 with real first backoff
    App-->>Host: Same ID, SUCCEEDED, two attempt records
    Host->>App: Cancel only test unique name; remove only test fixture
```

## Verified Behavior

The [existing recovery instrumentation](../android/agent/src/androidTest/java/dev/fleet/agent/EventSchedulerRecoveryInstrumentedTest.kt) records Android's boot count during preparation and after relaunch. In reboot mode it requires a strictly increased count. Numeric PIDs can be reused across boots, so this mode does not mistake PID inequality for proof of reboot. Same-boot recovery still requires unchanged boot count and a different PID.

The [host runner](../tools/check_android_enrollment.py) force-stops the agent and verifies the old PID is absent before reboot. Keeping the package stopped prevents probe execution before explicit relaunch. It records the old kernel boot ID, issues `adb reboot`, waits for ADB disconnection/reconnection, checks boot completion and storage, and requires a different canonical UUID kernel boot ID. A readiness deadline fails closed. It then invokes resume without enqueueing replacement work.

Resume checks that only the original work ID belongs to the random unique name, observes `SUCCEEDED`, and requires exactly two records numbered 0/1 with that ID and the resumed process's PID. Their monotonic timestamps must be at least twenty-nine seconds apart for configured thirty-second backoff. Only the two post-reboot attempt timestamps are subtracted; uptime across different boots is never compared. Host proof validation independently requires exact integer boot counts and the increase, matching IDs, attempts, process and backoff. Negative tooling tests reject unchanged/malformed kernel IDs, stale or wrong-typed boot counts, and prove an unchanged boot ID blocks resume while still attempting cleanup.

Cleanup waits for terminal state after cancelling only the random test unique name, deletes only that authorized private fixture and checks directory absence. Existing enrollment files, Keystore identity, Room event journal and WorkManager configuration/database are not reset. Normal terminal history remains for framework pruning. The debug probe reads no production identity or events. Package force-stop and emulator reboot are device-wide lab operations that can interrupt unrelated work; use only the explicitly authorized disposable AVD.

## Evidence

The [reboot replay](../artifacts/android-scheduler-reboot/20261017T193741.727388Z/replay.html) and [manifest](../artifacts/android-scheduler-reboot/20261017T193741.727388Z/manifest.json) pass all three separately invoked phases, 42 JVM tests, APK assembly and lint using private Java 17. The [host reboot proof](../artifacts/android-scheduler-reboot/20261017T193741.727388Z/reboot-verification.json) records changed kernel boot IDs. The [work proof](../artifacts/android-scheduler-reboot/20261017T193741.727388Z/recovery-verified.json) records boot count 30 to 31, original work ID `9c6265ca-629b-408f-becb-dbab14d65179`, attempts 0/1 in PID 1796 and a 30.086-second retry interval.

The [same-boot force-stop regression](../artifacts/android-scheduler-recovery/20261017T194052.795457Z/replay.html) passes with the same final source inputs. All 78 tooling tests pass, including seven focused enrollment-evidence tests. No failed reboot runtime bundle was produced. Backend/PostgreSQL runtime was not rerun. Prior force-stop milestone `d275b85` passed hosted CI [39061292141](https://github.com/michaelnwanna/custom-android-mdm-agent/actions/runs/39061292141); that result predates this reboot mode.

## Reproduction and Limits

With the authorized API 34 AVD ready and existing Java 17, Android SDK and Python dependencies configured, run from the repository root:

```sh
python3 tools/check_android_enrollment.py \
  --serial emulator-5554 --expected-avd Pixel_8 --scheduler-reboot
```

This mutually exclusive isolated mode checks the explicit AVD before reboot and retains exact input, report and APK fingerprints, command outcomes and public proof files. It does not wipe or initially boot the emulator. Cleanup failure fails the run; an unavailable device can prevent cleanup, so failed evidence must be inspected before retrying.

Android suppresses autonomous work for a force-stopped package until explicit launch. Foreground instrumentation can use GreedyScheduler. This proves persisted probe recovery after a controlled reboot and explicit relaunch, not BOOT_COMPLETED-triggered delivery, JobScheduler-only dispatch, connected-network transitions, actual Keystore/Room/backend event recovery, a process crash during upload, abrupt power loss, physical-device durability or production readiness. The unchanged normal startup hook can schedule existing enrollment work independently. Hosted CI compiles/lints instrumentation and runs JVM/tooling checks, not emulator reboot. Autonomous scheduling and isolated real-event recovery remain separate acceptance work.