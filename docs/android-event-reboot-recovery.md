# Enrolled Event Recovery Across Reboot

The combined API 34 lab now verifies the same isolated enrolled Keystore identity and persisted Room event across an actual non-wiping emulator reboot, followed by explicit instrumentation resume. This extends [same-boot process recovery](android-event-process-recovery.md), not automatic event delivery at boot.

```mermaid
sequenceDiagram
    participant Host as Trusted lab runner
    participant Old as Preparation process
    participant Disk as Isolated Keystore identity and Room journal
    participant API as Running HTTPS and PostgreSQL host
    participant New as Explicit post-reboot resume
    Old->>Disk: Complete enrollment and commit receipt event
    Old->>API: Worker body uploads event
    API->>API: Commit event and original acknowledgement
    API-->>Old: Test-only 503; worker returns retry
    Old->>Disk: Retain original identity, event and checkpoint
    Host->>Old: Force-stop; verify old PID absent
    Host->>Host: Reboot emulator; require changed kernel boot ID
    Host->>New: Explicit instrumentation resume
    New->>Disk: Require increased boot count; restore same identity and event
    New->>API: Replay unchanged event; recover original acknowledgement
    New->>Disk: Verify rollback, exact deletion, inbox and disabled-event retention
    Host->>New: Scoped fixture cleanup
```

## Verified Boundary

The [host runner](../tools/check_android_backend.py) retains the existing preparation/force-stop/resume protocol and adds `--event-reboot-recovery`. After preparation passes and the old PID is verified absent, it records the kernel boot ID, issues `adb reboot`, waits for ADB disconnection/reconnection and boot completion, checks storage readiness, and requires a different canonical kernel boot UUID before invoking resume. The package remains force-stopped until explicit launch, preventing delivery before the resumed test.

The [instrumentation fixture](../android/agent/src/androidTest/java/dev/fleet/agent/EnrollmentBackendInstrumentedTest.kt) records Android boot count in the private checkpoint and public preparation proof. Resume requires a strictly increased boot count. Host validation independently binds the two boot counts, original device/event IDs, exact stored wire SHA-256 and positive integer PIDs. PIDs can be reused across boots, so reboot mode deliberately does not require numerical PID inequality. Same-boot recovery still requires unchanged boot count and a new PID. Negative tooling tests reject stale/wrong-typed counts and prove an unchanged kernel boot ID blocks resume.

The existing recovery checks remain: restore the same random test Keystore identity without preparing/redeeming a new enrollment, verify preexisting inbox/outbox before duplicate command receipt, compare exact event wire JSON, recover the original committed acknowledgement, force a local deletion rollback, then delete the exact event through worker attempt 1 while retaining the inbox. Disabled-device upload and worker failure retain a new event. The resumed key remains non-exportable and proves possession against the one completed enrollment. The HTTPS/PostgreSQL host stays running across the Android reboot; backend restart is not tested.

Worker attempts are supplied by `TestWorkerBuilder`, not a persisted WorkRequest. Force-stop occurs after worker attempt 0 returns retry following the test-only post-commit 503. This is not an in-flight crash or real packet loss. Foreground instrumentation explicitly resumes execution; no autonomous BOOT_COMPLETED delivery, JobScheduler-only dispatch, OS-managed retry/backoff, network-constraint transition, abrupt power loss, physical/OEM durability or complete fleet workflow is established.

## Isolation and Reproduction

The fixture uses random test enrollment aliases and a separate Room database, never the default enrolled identity/journal. Its app-private checkpoint excludes the bootstrap credential and private key. Existing cleanup removes only recorded test aliases, namespace directories, database and checkpoint/preparation files, checking absence. The host invokes cleanup on success/failure and verifies private fixture files are gone. A device outage can prevent cleanup; inspect failed evidence before retrying. Force-stop and reboot can interrupt unrelated app/device work, so use only the authorized disposable AVD. No identity reset, WorkManager database clear or device wipe is used.

With the authorized API 34 emulator ready and existing Java 17, Android SDK, private PostgreSQL, .NET and Python dependencies configured, run from the repository root:

```sh
python3 tools/check_android_backend.py \
  --serial emulator-5554 --expected-avd Pixel_8 \
  --with-events --acknowledge-events --event-worker \
  --event-process-recovery --event-reboot-recovery
```

The new flag requires process-recovery mode and its existing worker/deletion/event prerequisites. MQTT remains a separate mutually exclusive run. The runner checks explicit AVD identity before reboot and retains exact input/report/APK fingerprints and command outcomes. PostgreSQL remains private/no-TCP and successful runs require zero test schemas and server shutdown. The runner does not initially boot or wipe an AVD. Hosted CI compiles/lints this instrumentation and runs JVM/tooling checks, not emulator/PostgreSQL reboot runtime.

## Evidence

The [reboot replay](../artifacts/android-backend-event-reboot/20261018T071406.097000Z/replay.html) and [manifest](../artifacts/android-backend-event-reboot/20261018T071406.097000Z/manifest.json) pass preparation, explicit resume and scoped cleanup, 42 JVM tests, APK build/lint and backend test-host compilation. [Kernel boot proof](../artifacts/android-backend-event-reboot/20261018T071406.097000Z/event-reboot-verification.json) records boot ID `2d1e2d48-41f7-4a16-897f-d33e4e8825d6` changing to `9b2acecd-6b95-481c-8ff6-53d9b0e9ae12`. The [resumed device proof](../artifacts/android-backend-event-reboot/20261018T071406.097000Z/device-public-proof.json) records boot count 33 to 34, preparation PID 2210 and resumed PID 1580, device `41f3975d-4280-4f9f-8b3e-ea28263880c0`, and original event `8634473d-5abb-4374-8b5a-aa4d925cc876` with unchanged wire hash. [Backend verification](../artifacts/android-backend-event-reboot/20261018T071406.097000Z/backend-verification.json) confirms one issued certificate, one suppressed acknowledgement and three recoveries of the original acknowledgement.

The [same-boot recovery regression](../artifacts/android-backend-event-process/20261018T073059.533065Z/replay.html) also passes against the same final source inputs. Both runs pass 42 JVM tests/build/lint and stop PostgreSQL with zero test schemas. All 82 tooling tests pass, including ten focused backend-evidence tests. No failed event-reboot runtime bundle was produced. Prior process-recovery commit `3b6b033` passed hosted CI [39066055117](https://github.com/michaelnwanna/custom-android-mdm-agent/actions/runs/39066055117); that result predates this reboot mode. Autonomous scheduled event delivery remains separate acceptance work.