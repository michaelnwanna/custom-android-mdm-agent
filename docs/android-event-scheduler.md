# WorkManager Scheduler Probe

Follow-up: [persisted probe recovery](android-scheduler-recovery.md) now verifies host-issued app force-stop, old-PID absence and same-ID completion after explicit new-process instrumentation relaunch. Earlier process-recovery limits below describe this initial scheduler milestone; autonomous dead-process/JobScheduler-only event delivery and reboot recovery remain unverified.

Two actual API 34 WorkManager scheduling scenarios now pass alongside the separately verified [event worker body](android-event-worker.md). The [debug-only probe](../android/agent/src/debug/java/dev/fleet/agent/SchedulerProbeWorker.kt) uses the existing WorkManager instance, real dispatch and real backoff timers. It does not use `TestWorkerBuilder`, a test driver, a fake clock or a forced `cmd jobscheduler run` command.

```mermaid
sequenceDiagram
    participant Test as API 34 instrumentation
    participant WM as Existing WorkManager / schedulers
    participant Probe as Debug-only no-credential probe
    Test->>WM: Enqueue random unique work with KEEP
    Test->>WM: Enqueue competing work with same unique name
    WM-->>Test: Only original work ID retained
    WM->>Probe: Dispatch attempt 0
    Probe-->>WM: Persist monotonic timestamp; Result.retry
    Note over WM: Real configured 30-second first backoff
    WM->>Probe: Dispatch attempt 1 with same work ID
    Probe-->>WM: Persist timestamp; Result.success
    WM-->>Test: Observe SUCCEEDED; verify records and elapsed interval
    Test->>WM: Separate random job with ten-minute initial delay
    Test->>WM: Cancel delayed job by exact ID
    WM-->>Test: Persisted CANCELLED; no attempt records
    Test->>WM: Cancel only test unique names; require terminal states
```

## Verified Behavior

The [instrumentation class](../android/agent/src/androidTest/java/dev/fleet/agent/EventSchedulerInstrumentedTest.kt) enqueues a random unique name and a competing `KEEP` request. It checks that the named work list contains only the original ID. The probe writes attempt 0 and attempt 1 records containing the work ID, attempt count and `SystemClock.elapsedRealtime` timestamp into its authorized random directory. Attempt 0 returns retry; attempt 1 returns success. Instrumentation observes the terminal state through LiveData, checks both records have the original ID, requires at least twenty-nine seconds between records for the configured thirty-second backoff, and checks exactly two attempt records. The small timing tolerance accommodates timestamp/transaction ordering, not shortened test backoff. Scheduler delay can be longer; the test fails if completion is not observed within one hundred seconds.

A second test enqueues a ten-minute-delayed probe, confirms it is enqueued, cancels its exact ID, observes and rereads `CANCELLED`, and checks that no attempt record was written. This proves cancellation before dispatch, not stopping a running network request. The prior event-worker test directly verifies `onStopped` cancellation at the deletion boundary; actual OS-driven in-flight event-worker cancellation remains unverified.

The probe is in `src/debug`, not `src/main` or the release source set. It accepts only a random UUID-shaped nonce and requires an existing authorization marker under its specific app-private `noBackupFilesDir` directory. It has no event, root, key, endpoint or enrollment inputs and never opens the normal event journal. Its JSON records contain only local timing and public IDs. It is a debug fixture, not a production sync worker or authentication API.

Tests do not replace WorkManager configuration, initialize a test singleton, clear its database, prune unrelated history, cancel all work, reset enrollment or wipe the device. Cleanup cancels only each random test unique name, waits for that operation, verifies all its work entries are terminal, then deletes only its fixture directory. Terminal WorkManager history may remain for normal framework pruning. The ordinary application startup hook is unchanged; the suite itself neither enables nor disables normal enrollment work.

## Evidence and Reproduction

The [scheduler replay](../artifacts/android-event-scheduler/20261017T184331.693658Z/replay.html) and [manifest](../artifacts/android-event-scheduler/20261017T184331.693658Z/manifest.json) pass both actual scheduler scenarios, 42 JVM tests, application/test APK assembly and lint using private Java 17. The [six-scenario event-worker regression](../artifacts/android-event-worker/20261017T184524.483588Z/replay.html) also passes against the same source revision. All 74 tooling tests pass; evidence parsing requires exactly two positive instrumentation results in scheduler mode. No failed scheduler runtime bundle was produced. The preceding worker commit `affa46c` passed hosted CI [39056906676](https://github.com/michaelnwanna/custom-android-mdm-agent/actions/runs/39056906676); that result predates this scheduler probe.

With the already authorized API 34 AVD ready and existing Java 17, Android SDK and Python prerequisites configured, run from the repository root:

```sh
python3 tools/check_android_enrollment.py \
  --serial emulator-5554 --expected-avd Pixel_8 --event-scheduler
```

The runner checks explicit serial/AVD identity and boot/storage readiness, retains exact sources, hashes, commands and test output, and requires exactly two passes. This mode cannot combine with the other isolated journal/worker modes. It does not boot or wipe an emulator. PostgreSQL and backend runtime are not needed or rerun by this scenario.

## Remaining Boundary

Instrumentation keeps the app process active; WorkManager may use its foreground GreedyScheduler as well as platform scheduling. This proves real WorkManager dispatch/backoff/state behavior, not JobScheduler-only delivery while the app is dead. Only the first backoff is timed: exponential sixty-second escalation is configured but not observed here. The probe has no network constraint, so connected/disconnected constraint transitions remain unverified even though the event-worker request has a verified connected constraint.

This is not scheduled execution of the real event worker, real Keystore/backend delivery under OS control, a process kill, reboot recovery, power-loss recovery or production deployment. Existing worker-body and Keystore/PostgreSQL evidence remain separate. Hosted CI compiles/lints this debug/test code but does not run the scheduler suite. Next acceptance work needs isolated persisted event/identity ownership across process or reboot boundaries before claiming automatic background event recovery.