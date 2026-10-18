# Android WorkManager Scheduled Event Delivery

This document verifies that WorkManager dispatches and retries event delivery with proper backoff timing and attempt counting.

## Verified Behavior

### Real WorkManager Dispatch

A debug-only `EnrolledEventSchedulerWorker` adapter restores an isolated test Keystore identity and delegates to the real `EventUploadWorker` body. WorkManager supplies the dispatch, attempt counts, and exponential backoff.

**Test**: Enqueue delayed work with a connected-network constraint and 30-second backoff. Observe two attempts.

- Attempt 0: completes, server returns a test-only `503` after commit.
- WorkManager retries with a measured 30.050-second interval.
- Attempt 1: receives the original acknowledgement and completes with success.
- The event is deleted from Room's outbox after successful acknowledgement.

**Evidence**: 
- Real WorkManager dispatch observed through worker execution callbacks
- Backoff interval measured: **30.050 seconds**
- Same work ID retained across attempts
- Exact acknowledgement bytes recovered on retry

### Scheduler Behavior

Earlier probes verified that WorkManager honors:
- Unique `KEEP` deduplication (no duplicate work IDs)
- Cancellation of delayed work before dispatch
- Actual process recovery after app restart
- Reboot persistence across non-wiping emulator reboot

**Not Verified**: JobScheduler-only dispatch, automatic background resumption while the app is force-stopped, autonomous dead-process execution, or real production event delivery.

## Test Scope

The `EnrolledEventSchedulerWorker` adapter:
- Accepts only an authorized app-private test fixture directory
- Restores the isolated Keystore identity and Room database
- Delegates to the real worker body for upload and deletion
- Cleans up immediately after the worker returns

**Foreground Instrumentation Limitation**: Tests run in the foreground APK process and may use `GreedyScheduler` for immediate execution. This verifies the worker body's logic and the debug adapter's restoration, but does not establish OS-level scheduling behavior at boot or during service conditions.

## Test Failures Retained

The earlier scheduled-delivery attempt failed when the system lacked Java 17. The failure is retained for reference; the corrected run (using Java 21) passed all six scenarios.

## Documentation

- **Worker Runbook**: EventUploadWorker.kt implementation, retry policy, and cancellation behavior
- **Scheduler Probe Runbook**: Debug probe implementation and earlier verification of actual WorkManager behavior
- **Process Recovery Runbook**: Real Keystore/Room/HTTPS/PostgreSQL recovery across process termination and relaunch
- **Reboot Recovery Runbook**: Recovery across non-wiping emulator reboot with boot ID change

## What Remains

- **JobScheduler Autonomous Delivery**: Test whether the system triggers event delivery automatically while the app is stopped or after reboot
- **Real Production Scheduling**: Verify that enrolled devices deliver events with retries in a live environment, not just in instrumentation
- **Backoff Confirmation**: Validate that real exponential backoff respects OS power management and scheduled-job limits
- **Event Publishing**: The worker retries delivery, but commands are still provisioned only in tests, not through a real policy/deployment flow

---

**Test Run**: 20261018T081252.118333Z  
**JVM Tests**: 42 passing  
**APK Build**: Passed  
**Lint**: Passed  
**Instrumentation**: Passed  
**PostgreSQL**: Stopped cleanly (zero test schemas)