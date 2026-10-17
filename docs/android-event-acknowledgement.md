# Authenticated Event Acknowledgement Deletion

Follow-up: the [bounded event worker](android-event-worker.md) now calls this operation with controlled cancellation and a request deadline. Scheduling hooks are implemented, but actual OS scheduling/recovery remains unverified. Earlier absent-worker/cancellation statements below describe the original caller-driven milestone; authenticated deletion rules remain unchanged.

[CommandJournal.uploadAndAcknowledge](../android/agent/src/main/java/dev/fleet/agent/CommandJournal.kt) now uploads one existing pending event and removes that exact outbox row only after [the HTTPS transport](android-event-upload.md) authenticates the server and validates the matching acknowledgement. This is an internal synchronous caller-driven operation, not a worker, UI action or enabled production synchronization path. The transport-only `upload` method and the original retention-only replay mode remain nondeleting.

```mermaid
sequenceDiagram
    participant Journal as CommandJournal
    participant Room as Room inbox and outbox
    participant TLS as Pinned HTTPS / enrolled Keystore
    participant PG as Backend PostgreSQL
    Journal->>Room: Require device-bound exact pending row
    Journal->>TLS: Upload original stored bytes outside transaction
    TLS->>PG: Authenticated event ingestion
    PG-->>TLS: Commit event and original acknowledgement
    TLS-->>Journal: Only trusted 200 + strict matching device/event ACK
    Journal->>Room: Begin short local transaction; re-read exact event
    alt Same row remains
        Journal->>Room: Delete exact device/event/command/wire row; require one deletion
    else Concurrent caller already removed row
        Note over Journal,Room: No-op success; authenticated upload still occurred
    else Row changed during upload
        Note over Journal,Room: Fail and preserve changed row
    end
    Journal->>Room: Commit; retain command inbox receipt
    Note over Journal,Room: Duplicate command returns original receipt without recreating event
```

## Mutation Boundary

The journal requires the identity and stored row to belong to its device and checks all stored fields against the current pending row before sending. The caller supplies the already enrolled identity and trusted server root, as for the existing uploader; neither an event body nor an acknowledgement selects the trust root. The operation does not accept a parsed acknowledgement, bytes, transport callback or caller-provided authentication boolean as deletion authority. The strict decoder alone is not authentication.

The network operation completes outside any Room transaction. Only a pinned, hostname-verified HTTPS exchange returning status 200, supported JSON media/encoding, bounded strict acknowledgement and matching device/event IDs can reach deletion. Exceptions from TLS, response validation or non-200 statuses leave the pending row untouched. MQTT PUBACK never reaches this boundary.

After upload, a short Room transaction re-reads the device/event row. If present, every field must still equal the uploaded snapshot, and the exact `DELETE` must affect one row. A changed row fails rather than deleting new content. If another successful caller has already removed it, the transaction is a no-op and returns the valid acknowledgement. An event already absent at the initial precheck is rejected before sending. Overlapping calls may therefore both upload the same immutable event; durable backend event-ID deduplication returns its original acknowledgement. The returned value is an authenticated acknowledgement, not a count of local rows removed.

A delete/commit failure propagates and leaves the event retryable. There is intentionally no distributed transaction across HTTPS and SQLite: if the server has committed but the client has not removed the row, replay recovers the original acknowledgement. No database lock spans network I/O. The inbox is never deleted, so a later identical command returns its original receipt/event ID without recreating the acknowledged event. Insertion conflicts and initial receipt behavior are unchanged. DAO access and the app-private database remain trusted internal code, not protection against malicious SQL or a compromised app process.

Only DAO queries changed; the exported Room schema remains version 1 and requires no migration. Application code must invoke this blocking operation off the main thread and handle errors without logging wire bodies. The operation does not introduce scheduling, retry policy, batching, cancellation, queue fairness, retention quotas or inbox reclamation.

## Verified Evidence

- [Five isolated API 34 SQLite/mTLS scenarios](../artifacts/android-event-acknowledgements/20261017T172902.453459Z/replay.html) verify exact-row/device isolation, preserved inbox and duplicate command after reopen, untrusted/malformed/mismatched/error replies retaining the event, forced delete rollback and retry, two overlapping uploads, and preservation of a row modified while the HTTPS reply is in flight. These use software-key certificates and MockWebServer on Android, not the real Keystore/backend.
- [Combined API 34 Keystore/Room/HTTPS/PostgreSQL deletion replay](../artifacts/android-backend-event-deletion/20261017T173036.181623Z/replay.html) verifies a test-only 503 after server commit leaves the row, Room reopen recovers it, a transport-only retry recovers the original acknowledgement without deletion, an authenticated upload plus forced local delete failure preserves it, and a later authenticated retry deletes it. After another reopen, duplicate command delivery recovers the original inbox receipt without creating an event. A new local event remains pending when disabled-device authentication returns 401.
- Independent [backend proof](../artifacts/android-backend-event-deletion/20261017T173036.181623Z/backend-verification.json) requires one stored original event/acknowledgement, one suppression and three identical duplicate acknowledgements (retention check, failed-delete attempt, successful-delete attempt). The [device proof](../artifacts/android-backend-event-deletion/20261017T173036.181623Z/device-public-proof.json) reports deletion, rollback/reopen, inbox retention and disabled-event preservation; the host/runner reject missing flags or wrong counters. Android SQLite assertions establish local deletion; the backend independently establishes durable original acknowledgement recovery, not direct access to the Android database.
- [Original retention-only replay regression](../artifacts/android-backend-events/20261017T173242.088534Z/replay.html) still retains the original event after valid 200 and disabled 401. [Six original journal scenarios](../artifacts/android-command-journal/20261017T173404.745228Z/replay.html) also pass unchanged.

Each final runner passes 37 JVM tests, application/test APK assembly and lint with private Java 17. All 73 Python tooling tests pass. Both final backend labs leave zero test schemas and stop PostgreSQL. Random test-only Room databases and Keystore aliases are cleaned without replacing the normal enrollment identity or wiping the AVD. Earlier passing snapshots [171934](../artifacts/android-event-acknowledgements/20261017T171934.613186Z/replay.html), [172147](../artifacts/android-backend-event-deletion/20261017T172147.668019Z/replay.html) and [172706](../artifacts/android-event-acknowledgements/20261017T172706.451018Z/replay.html) are retained as intermediate evidence before the final transaction/race suite. No failed runtime snapshot was produced in this milestone.

## Reproduce

With the explicitly authorized API 34 emulator already booted and the existing Java 17, Android SDK, Python dependencies and private PostgreSQL lab configured, run from the repository root:

```sh
python3 tools/check_android_enrollment.py \
  --serial emulator-5554 --expected-avd Pixel_8 --event-acknowledgements
python3 tools/check_android_backend.py \
  --serial emulator-5554 --expected-avd Pixel_8 --with-events --acknowledge-events
```

`--acknowledge-events` requires `--with-events` and selects a separate evidence directory. It sets test-only `FLEET_TEST_EVENT_ACK_DELETE=1`; the host explicitly advertises this mode and requires its proof. Omitting it preserves the earlier retention-only scenario. MQTT and event modes remain mutually exclusive. The isolated acknowledgement mode cannot be combined with `--command-journal` and requires exactly five instrumentation passes. All runners require the supplied serial/AVD identity and readiness; none boot or wipe the device.

## Remaining Limits

Post-commit 503 suppression is not a real dropped connection or lost packet; Room close/reopen is not a process kill. Reboot, power loss, multi-process uploader contention, disk exhaustion, actual response loss, production lifecycle execution/command creation, production workers and full offline fleet acceptance remain unverified. Hosted CI compiles the instrumentation classes and runs tooling/JVM checks; it does not execute these Android/PostgreSQL labs. The next runtime integration needs explicit worker/lifecycle scheduling and bounded retry/cancellation semantics, not direct deletion from an arbitrary decoded reply.