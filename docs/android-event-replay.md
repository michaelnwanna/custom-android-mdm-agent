# Combined Android Event Replay

The opt-in `--with-events` mode of [the combined runner](../tools/check_android_backend.py) now verifies one real API 34 Room/Android Keystore/HTTPS/PostgreSQL event roundtrip and original acknowledgement recovery after a deliberately suppressed reply. This is an isolated test scenario, not a production upload worker, deletion implementation or packet-loss test.

```mermaid
sequenceDiagram
    participant Room as Isolated Android Room journal
    participant Android as Keystore HTTPS uploader
    participant Host as Test host / real event endpoint
    participant PG as PostgreSQL
    Room->>Android: Original persisted event
    Android->>Host: Enrolled-certificate POST
    Host->>PG: Check trusted ownership; commit event and original ACK
    PG-->>Host: Commit succeeds
    Host-->>Android: Test-only 503 instead of acknowledgement
    Android->>Room: Verify unchanged event; close and reopen database
    Room->>Android: Same event bytes and ID
    Android->>Host: Retry same event
    Host->>PG: Recover committed original ACK
    Host-->>Android: 200 with original ACK
    Android->>Room: Verify event still retained; no deletion
    Note over Android,Host: Existing identity diagnostic then commits disable
    Android->>Host: Retry with disabled identity
    Host-->>Android: 401
    Android->>Room: Verify event still retained
```

## Test Controls

The [test host](../tools/android-enrollment-host/Program.cs) enables this path only with `FLEET_TEST_EVENT_UPLOAD=1`, set by the opt-in runner. It chooses a random command/correlation binding and registers ownership after successful enrollment, before giving the test permission to upload. This trusted fixture path is not a public command-registration API. The test-only certificate-identity decorator allows event requests without disabling the device; the existing identity diagnostic still performs alternate-certificate rejection and commits disable on its first accepted request.

The ingestion decorator calls real `PostgresCommandEvents.IngestAsync`. After its first successful commit, it retains the original acknowledgement in memory and throws an `IOException`, producing a generic 503 instead of returning the acknowledgement. Retry must be a duplicate with the identical stored acknowledgement. The host independently compares the Android-reported device/event/timestamp against that original and the single PostgreSQL event row with the trusted command/correlation binding. Exactly one suppression and one recovery are required. This deliberately tests server-committed/client-unacknowledged state, not a dropped TCP connection, lost packet or ambiguous database commit.

The [instrumentation scenario](../android/agent/src/androidTest/java/dev/fleet/agent/EnrollmentBackendInstrumentedTest.kt) uses random test-only enrollment aliases and a separate random Room database. It constructs a `REQUEST_SYNC` test command, persists its receipt, uploads with the non-exportable enrolled key, verifies unchanged rows after 503, closes/reopens Room and uploads the same row. After strict acknowledgement validation, it verifies the row remains unchanged. After committed disable, a further upload returns 401 and the row is still unchanged. Test cleanup deletes only its own database and test aliases; normal enrollment identity and kiosk state are not reset.

The runner requires the explicit emulator serial and matching AVD name, boot/storage readiness, one passing instrumentation scenario, independent host verification, zero remaining test schemas and PostgreSQL shutdown. Event replay and MQTT modes cannot be combined in one run because they use separate command fixtures/databases. Public configuration remains allowlisted and excludes bootstrap secrets. The new proof-validator regression rejects missing counters, boolean-as-integer counters, malformed/mismatched acknowledgement fields and retained-event ID mismatch.

## Verified Evidence

The [final replay](../artifacts/android-backend-events/20261017T165647.089929Z/replay.html), [manifest](../artifacts/android-backend-events/20261017T165647.089929Z/manifest.json), [device proof](../artifacts/android-backend-events/20261017T165647.089929Z/device-public-proof.json) and [backend proof](../artifacts/android-backend-events/20261017T165647.089929Z/backend-verification.json) pass 37 JVM tests, APK builds/lint and one actual API 34 instrumentation scenario. They verify real Keystore mTLS, one committed original event/acknowledgement, post-commit 503, Room close/reopen/replay, exact original acknowledgement recovery, retained rows and disabled upload rejection. Enrollment provisioning/audit, exact enrollment-response retry and public-key possession checks still pass. All 72 Python tooling tests pass. The private no-TCP PostgreSQL lab leaves zero test schemas and stops cleanly; private Java 17 is used.

The [first passing replay](../artifacts/android-backend-events/20261017T165408.405390Z/replay.html) precedes explicit event-specific manifest scope/request metadata and is retained separately. No failed runtime snapshot was produced in this milestone. The preceding component's [JVM-only verification](android-event-upload.md) remains distinct from this combined runtime proof.

From the repository root, with the explicitly authorized API 34 emulator already ready:

```sh
python3 tools/check_android_backend.py \
  --serial emulator-5554 --expected-avd Pixel_8 --with-events
```

Use the configured Java 17 runtime and the runner's Python dependencies. The runner does not boot/wipe an emulator or alter its normal identity; it installs debug/test APKs and uses isolated app-private test files. Do not point it at an arbitrary or production device.

## Remaining Boundary

Outbox deletion is still absent. A future journal acknowledgement operation needs authenticated matching acknowledgement provenance, device/event-scoped mutation and transaction/reopen tests, including malformed or unrelated replies leaving rows untouched. There is no worker, retry/backoff scheduler, UI upload action or production command creation. Real response loss, process kill, reboot, power loss, concurrent uploader behavior, retention policy, lifecycle execution, production deployment and full offline fleet acceptance remain unverified. Hosted CI compiles the scenario but does not run the emulator/PostgreSQL lab.