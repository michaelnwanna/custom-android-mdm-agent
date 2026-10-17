# Persisted Work Recovery After Force-Stop

Follow-up: the [reboot probe](android-scheduler-reboot.md) now verifies changed kernel boot ID and increased Android boot count, followed by explicit same-ID resume. The reboot exclusions below describe this original same-boot milestone; autonomous boot dispatch and actual event recovery remain unverified.

The debug-only WorkManager probe verifies persistence across a host-issued app force-stop followed by explicit instrumentation relaunch on API 34. This extends the [scheduler scenarios](android-event-scheduler.md), not production event-worker runtime acceptance.

```mermaid
sequenceDiagram
    participant Host as Trusted lab host
    participant Old as Preparation process
    participant WM as Persisted WorkManager database
    participant New as Explicitly relaunched process
    Host->>Old: Instrument preparation with random nonce
    Old->>WM: Enqueue unique probe with 45-second initial delay
    Old-->>Host: Original work ID and preparation PID
    Host->>Old: am force-stop dev.fleet.agent
    Host->>Host: Require old /proc/PID directory absent
    Host->>New: Explicit instrumentation relaunch; no enqueue
    New->>WM: Observe original persisted work ID
    WM->>New: Dispatch attempt 0; retry
    Note over WM,New: Real configured 30-second first backoff
    WM->>New: Dispatch attempt 1; success
    New-->>Host: Same ID, new PID, timestamps, SUCCEEDED
    Host->>New: Cancel only test unique name; delete only test fixture
```

## Verified Boundary

The [three-phase instrumentation class](../android/agent/src/androidTest/java/dev/fleet/agent/EventSchedulerRecoveryInstrumentedTest.kt) prepares one random unique request using the existing WorkManager singleton. Its 45-second initial delay prevents immediate dispatch while the host reads the prepared record. The [host runner](../tools/check_android_enrollment.py) requires a positive preparation result, issues force-stop and fails unless the old PID's directory is absent. Only then does it explicitly launch resume.

Resume does not enqueue replacement work. It requires the original ID to be the unique name's only entry, observes terminal success and verifies exactly two attempt records numbered 0 and 1. Both must identify the new process and original work ID, with at least twenty-nine seconds between monotonic timestamps for the configured thirty-second backoff. Completion has a 110-second observation limit. The host independently validates exact integer fields, IDs, distinct positive PIDs, terminal state and interval. A tooling regression proves failed old-process absence verification blocks resume while still requesting cleanup.

The [debug probe](../android/agent/src/debug/java/dev/fleet/agent/SchedulerProbeWorker.kt) requires a random authorized app-private directory and reads no enrollment identity, keys, roots, endpoint or event journal. Cleanup cancels only the random test unique name, waits for terminal entries and removes only its fixture; the host verifies directory absence. Cleanup failure fails the run, including when an earlier phase also failed. Normal terminal WorkManager history remains for framework pruning. No database clear, configuration replacement, test clock, forced scheduler execution, identity replacement or device wipe is used.

Force-stop is package-wide and can interrupt other agent work: use only the explicitly authorized disposable lab emulator. It is not a simulated process crash. Android prevents autonomous work for a force-stopped package until explicit launch clears the stopped state. Foreground instrumentation can use GreedyScheduler. This is not automatic dead-process delivery, JobScheduler-only dispatch, a kill during execution, reboot/power-loss recovery, constraint-transition coverage or real Keystore/Room/backend event recovery. The unchanged ordinary app-startup hook may schedule normal existing enrollment work; this fixture neither enables nor disables that work.

## Evidence

The [final replay](../artifacts/android-scheduler-recovery/20261017T190317.047992Z/replay.html) and [manifest](../artifacts/android-scheduler-recovery/20261017T190317.047992Z/manifest.json) pass all three separately invoked phases, 42 JVM tests, APK assembly and lint with private Java 17. The [recovery proof](../artifacts/android-scheduler-recovery/20261017T190317.047992Z/recovery-verified.json) records PID 2415 replaced by PID 2472, work ID `fc1bfb16-0651-4c35-ad34-f01e5facca46`, attempts 0/1 and a 30.038-second interval. The [host stop record](../artifacts/android-scheduler-recovery/20261017T190317.047992Z/process-stop-verification.json) accompanies the successful absence-check command in the manifest.

The [initial recovery run](../artifacts/android-scheduler-recovery/20261017T185848.734405Z/replay.html) also passed; the final run adds the host failure-path test to retained inputs. The [existing scheduler regression](../artifacts/android-event-scheduler/20261017T190117.710883Z/replay.html) passes both scenarios against the same final executable sources. All 76 tooling tests pass. No failed device recovery bundle was produced; a local Python indentation error was caught and repaired by focused tests before any device run. Backend/PostgreSQL runtime was not rerun.

## Reproduction

With the authorized API 34 AVD already ready, Java 17, Android SDK and Python dependencies configured, run from the repository root:

```sh
python3 tools/check_android_enrollment.py \
  --serial emulator-5554 --expected-avd Pixel_8 --scheduler-recovery
```

This isolated mode cannot combine with other journal/worker modes. It verifies AVD identity/readiness and retains source/report/APK hashes, commands and public proofs. It does not boot or wipe an AVD. Hosted CI compiles/lints the phases and runs proof/orchestration tooling checks, not emulator recovery. Isolated actual event/identity recovery and autonomous scheduler dispatch remain acceptance work before claiming background fleet delivery.