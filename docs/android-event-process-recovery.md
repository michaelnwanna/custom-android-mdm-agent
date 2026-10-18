# Enrolled Event Recovery Across Process Restart

Reboot follow-up: [enrolled event reboot recovery](android-event-reboot-recovery.md) now verifies the same isolated identity/event across actual reboot and explicit resume. Earlier reboot exclusions below describe this same-boot milestone; autonomous boot dispatch and OS-managed retry remain unverified.

The combined API 34 lab now verifies a real enrolled Android Keystore identity and Room event surviving host-issued app force-stop, followed by explicit instrumentation relaunch and worker-body recovery against the same running HTTPS/PostgreSQL host. This extends the [bounded worker acceptance](android-event-worker.md); no production worker configuration or default enrolled identity is changed.

```mermaid
sequenceDiagram
    participant Host as Trusted lab runner
    participant Old as Preparation process
    participant Store as Isolated Keystore identity and Room journal
    participant API as HTTPS mTLS and PostgreSQL test host
    participant New as Explicitly relaunched process
    Old->>API: Complete enrollment using random test key
    Old->>Store: Commit command and initial receipt event
    Old->>API: Worker attempt 0 uploads stored event
    API->>API: Commit event and original acknowledgement
    API-->>Old: Test-only 503 after commit
    Old->>Store: Verify unchanged pending event; retain fixture
    Old-->>Host: Passed preparation, PID, event ID and wire hash
    Host->>Old: Force-stop; require old PID absent
    Host->>New: Explicit resume invocation
    New->>Store: Restore same key/certificate and existing inbox/outbox
    New->>API: Replay unchanged stored event
    API-->>New: Recover exact original acknowledgement
    New->>Store: Verify forced delete rollback; worker retry deletes exact event
    New->>Store: Verify retained inbox, duplicate command and disabled-event retention
    Host->>New: Cleanup only random test identity/database/checkpoint
```

## Preparation and Resume

The [instrumentation fixture](../android/agent/src/androidTest/java/dev/fleet/agent/EnrollmentBackendInstrumentedTest.kt) uses a random `integration-UUID` enrollment namespace and `test-mqtt-command-UUID.db`, never the default enrolled key or journal. Preparation records those fixture names before creating keys, completes actual HTTPS enrollment, commits a typed command receipt, and invokes the real event worker body with its existing internal test session factory. The test host commits the event before deliberately returning its first 503. The worker returns retry and the exact pending row remains.

The app-private checkpoint contains public roots/configuration, the original CSR/certificate response, command/event wire JSON, fixture names, preparation PID and verified retry result. It explicitly excludes the bootstrap credential; the original private input is consumed before enrollment. It contains no private key. The public preparation record contains only version, PID, device/event IDs and wire SHA-256. The [host runner](../tools/check_android_backend.py) requires the preparation test to pass, issues force-stop, checks the old `/proc/PID` is absent, and only then explicitly invokes resume.

Resume requires a different PID and restores the existing test identity without preparing a new key or redeeming a new enrollment. It checks the preexisting inbox and pending event before submitting the duplicate command, requires duplicate receipt semantics, and compares the original event ID and exact stored wire JSON. The resumed TLS requests use the enrolled non-exportable key; the existing signature/certificate proof is independently checked by the host against the one completed enrollment.

The resumed scenario then exercises the existing original-acknowledgement recovery, local delete-trigger rollback, reopen/retry, authenticated exact-event deletion through worker attempt 1, retained inbox/deduplication and disabled-device retention checks. Attempt numbers are supplied by `TestWorkerBuilder`; this is real worker-body execution, not WorkManager scheduling or a persisted WorkRequest. The host independently binds public preparation and resume PIDs, device/event IDs and wire hashes, alongside backend verification of one committed event and exact original acknowledgement recovery.

## Cleanup and Reproduction

Cleanup closes Room handles, removes only the recorded random test database, deletes only the two test enrollment aliases and their namespace directories, and checks key/file absence. The host invokes a separate cleanup test even on failure and verifies checkpoint/preparation-file absence. Existing normal identity, default database, WorkManager state and AVD data are not cleared. A failed/unavailable device can prevent cleanup; inspect retained command evidence before retrying rather than deleting unknown state. Force-stop is package-wide and can interrupt other agent work, so use only the authorized disposable emulator.

With the authorized API 34 AVD already ready and existing private PostgreSQL, Java 17, Android SDK, .NET and Python prerequisites configured, run from the repository root:

```sh
python3 tools/check_android_backend.py \
  --serial emulator-5554 --expected-avd Pixel_8 \
  --with-events --acknowledge-events --event-worker --event-process-recovery
```

This mode requires worker/deletion/event flags and cannot combine with MQTT mode. The backend remains running across the Android process boundary. PostgreSQL uses the existing private no-TCP lab setup; successful runs verify zero test schemas and stop the server. Public evidence retains exact source snapshots, command results and hashes; bootstrap/private host inputs remain excluded.

## Evidence and Limits

The [process-recovery replay](../artifacts/android-backend-event-process/20261017T200613.109156Z/replay.html) and [manifest](../artifacts/android-backend-event-process/20261017T200613.109156Z/manifest.json) pass preparation, resume and cleanup, 42 JVM tests, APK build/lint and backend test-host compilation. The [preparation proof](../artifacts/android-backend-event-process/20261017T200613.109156Z/event-process-prepared.json), [verified stop](../artifacts/android-backend-event-process/20261017T200613.109156Z/event-process-stop.json) and [resumed proof](../artifacts/android-backend-event-process/20261017T200613.109156Z/device-public-proof.json) record PID 2188 replaced by PID 2249, device `f5cf3355-91c4-499a-85a4-8ce3b720529d`, event `71d51dd9-342d-43f7-9de6-53e7c537169c` and identical wire hash. [Backend verification](../artifacts/android-backend-event-process/20261017T200613.109156Z/backend-verification.json) confirms one issued certificate, one suppressed acknowledgement and three recoveries of the original acknowledgement.

The [single-process worker regression](../artifacts/android-backend-event-worker/20261017T200832.763630Z/replay.html) also passes against the same final source inputs. Both bundles report 42 passing JVM tests and stopped PostgreSQL servers. All 80 tooling tests pass, including negative identity/event/hash/PID binding and old-process-presence guards. No failed process-recovery runtime bundle was produced. Prior reboot-probe commit `5d8a16a` passed hosted CI [39063823902](https://github.com/michaelnwanna/custom-android-mdm-agent/actions/runs/39063823902); that result predates this event recovery mode.

This is controlled force-stop after the worker has returned retry, not a crash during upload, database commit or local deletion. The 503 is deliberate test-host response suppression, not lost packets or a real network disconnect. Instrumentation explicitly relaunches the package and supplies worker attempts; no autonomous delivery, actual scheduler retry/backoff, JobScheduler-only execution, event recovery across reboot, backend restart, abrupt power loss, physical/OEM durability or full fleet workflow is established. Scheduler/reboot probe evidence remains separate and must not be combined into an untested end-to-end claim. Hosted CI compiles these scenarios and runs JVM/tooling checks, not the emulator/PostgreSQL recovery runtime.