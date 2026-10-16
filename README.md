# Offline-First Android Fleet Management

A Kotlin Android Device Policy Controller with a planned C# / ASP.NET Core control plane and React management console for intermittently connected device fleets.

## Status

Implementation has started with a minimal Android feasibility lab: a test-only Device Owner agent, a separate versioned sample app, and a prerequisite checker. Both debug APKs compile and pass Android lint with the checksum-pinned Gradle 8.9 wrapper. Device Owner provisioning, genuine lock task mode, Home/Recents containment, and visible lab recovery passed on a reset API 34 emulator. See the [runtime verification record](docs/android-runtime-verification.md) for evidence and limits.

Phase 1 is accepted for API 34 development feasibility under the [revised acceptance scope](docs/phase-one-acceptance.md); Phase 2 foundation work may proceed. Current-stable compatibility is deferred to hardening and remains required before broader support or production-readiness claims. Selected settings restrictions and per-device P-256 ECDSA signed maintenance exit are implemented; no unauthenticated UI exit remains. Debug/ADB pairing is still trusted lab only, not production maintenance; preserve the paired operator key securely, since key loss requires authorized trusted-emulator ADB reprovisioning, not a UI bypass. The [latest five-checkpoint maintenance replay](artifacts/android/20261016T110746.710228Z-phase-one/replay.html) and [manifest](artifacts/android/20261016T110746.710228Z-phase-one/manifest.json) passed restriction application/cleanup and unsigned, tampered, and consumed-challenge rejection, plus build/lint and native video validation. Eight JVM MaintenanceProof tests and thirty-one Python tests passed, including four new operator-pairing regression tests. The [seven-checkpoint MQTT lifecycle replay](artifacts/android/20261016T104119.390614Z-mqtt-lifecycle/replay.html) and [manifest](artifacts/android/20261016T104119.390614Z-mqtt-lifecycle/manifest.json) verify TLS roundtrip, receive/ack while the sample was foreground and LOCKED, broker-offline containment, reconnect with a fresh ACK, process-death containment without an immortal socket, explicit restart, and explicit stop. This is a test-only, user-started `START_NOT_STICKY` foreground diagnostic using anonymous synthetic probes over loopback aMQTT TLS, not production transport.

A locally staged, agent-driven sample v1-to-v2 PackageInstaller update is verified on API 34, including negative identity/version/signer/invalid-APK checks and v2 kiosk relaunch. The [fresh sixteen-checkpoint update replay](artifacts/android/20261016T104623.288748Z-update/replay.html) and [manifest](artifacts/android/20261016T104623.288748Z-update/manifest.json) passed current build/lint invocations, twenty-seven Python tests, restrictions, and signed maintenance recovery. Persisted kiosk intent restored enabled and disabled state across non-wiping cold boots; the [six-checkpoint warm-reboot replay](artifacts/android/20261016T104301.613674Z-reboot/replay.html) and [manifest](artifacts/android/20261016T104301.613674Z-reboot/manifest.json) now verify two genuine warm reboots, automatic enabled restoration/Home containment, then disabled no-restoration/persisted exit. All four new bundles passed native video validation. The [earlier warm-reboot failure](artifacts/android/20261016T093407.811439Z-reboot/replay.html) remains failed; [recorded tests](docs/test-evidence.md) preserve it and other failures. These API 34 results do not establish physical-device/OEM reliability or current-stable compatibility. Production MQTT transport, signed release delivery, durable synchronization, backend services, and the web console remain planned.

### Phase 2 Contract Foundation

The [versioned contracts](contracts/README.md) define strict command, command-state event, durable-ingestion acknowledgement and enrollment HTTP shapes. The [OpenAPI enrollment contract](contracts/openapi.json) and [enrollment rules](contracts/enrollment.md) specify bootstrap-token/CSR boundaries, server-assigned identity and safe response recovery. The [latest recorded contract check](artifacts/contracts/20261016T125637.174195Z/replay.html) passed six unittest checks covering thirteen valid and fifty-eight rejection fixtures, OpenAPI declarations and size/chain boundaries, with exact input snapshots, hashes and dependency versions retained. The [first messaging-only run](artifacts/contracts/20261016T124733.464095Z/replay.html) remains historical evidence. Contracts alone do not establish authentication, lifecycle ordering or durable delivery.

The [.NET enrollment foundation](backend/README.md) verifies PKCS#10 proof of possession and enrollment-bound bootstrap tokens, and includes an EF Core/Npgsql pending-enrollment model, migration and exclusive claim implementation fixing server identity and CSR digest. Its [forty-six-test component run](artifacts/backend/20261016T144849.949564Z/replay.html) passed alongside six installer/lifecycle tests. The [real PostgreSQL 17.11 run](artifacts/postgres/20261016T151840.312440Z/replay.html) also passed migration, eight competing claims, context-reopen retry, conflict, rollback, expiry and selected constraint checks. Sources, dependencies, hashes and TRX results are retained. The [private database workflow](docs/postgres-lab.md) uses a Unix socket without TCP; the server stopped after testing. Docker was not used or managed by this workflow, and emulators remain stopped. Same-CSR claims retain in-progress IDs, not a completed certificate response. Process-restart/power-loss recovery, HTTP endpoints/authentication, completed issuance/recovery, functional enrollment and production transport remain pending.

### Current-Stable Compatibility Attempt

Current-stable compatibility is a deferred hardening gate, not a Phase 2 blocker or a claimed pass. Official stable Android 17 / API 37.0 Google APIs x86_64 revision 6 (extension 22) was installed and booted on the separate `Fleet_Phase1_API37` AVD, serial `emulator-5556`, with emulator 37.2.12. SDK Command-line Tools 23 were installed with checksum verification on the Intel Mac. The original API 34 `Pixel_8` was shut down intact, without a wipe; its thirty-one Python/eight JVM passes and runtime evidence are unchanged.

Earlier API 37 provisioning and media failures remain preserved in the [evidence guide](docs/test-evidence.md#android-17-current-stable-compatibility-attempt). A later [standard provisioning retry succeeded](artifacts/android/20261016T120217.950135Z-api37-platform-diagnostics/manifest.json), and Device Owner state was confirmed. After an explicitly recorded System UI ANR recovery, the [maintenance attempt](artifacts/android/20261016T120711.924506Z-phase-one/replay.html) passed restriction application, unsigned exit denial, and tampered-signature denial, then failed when UI capture exited 137 and recording finalization timed out. Android subsequently lost Activity Manager; [signed recovery failed](artifacts/android/20261016T121330.133823Z-recover/manifest.json). The AVD was stopped without wiping; authenticated exit is unconfirmed and enabled kiosk intent may restore next boot. Complete API 37 maintenance, reboot, update, MQTT, and validated-video coverage remain unverified. No root, hidden API, account/setup-check bypass, or device wipe was used; the initialization failure's cause is not proven.

Further [recovery experiments](docs/test-evidence.md#controlled-recovery-experiments) found that four CPUs did not eliminate software-rendered ANRs. Host rendering exposed GLES 3.0, below the emulator's API 37 GLES 3.1 requirement, so no acceptance suites ran in that configuration. The later retained state was Device Owner present, kiosk intent disabled, and lock task `NONE`; this supersedes the earlier unknown intent warning, not the failed signed-recovery result. Both emulators are stopped. Completing the deferred hardening gate requires a stable supported current-stable runtime.

## Architecture

This diagram separates implemented API 34 lab diagnostics from the planned end-to-end system. The isolated loopback MQTT probe is not production transport or backend infrastructure. The numbered flow below describes the target design, not an implemented integration.

```mermaid
flowchart TB
	Admin[Administrator]

	subgraph Management["Management plane - planned"]
		Console[React management console]
		API[ASP.NET Core API]
		DB[(PostgreSQL: desired state, commands, audit, outbox)]
		Publisher[Durable command publisher]
		Ingest[Telemetry and result ingestion]
		Artifacts[Signed release metadata and immutable APK storage]
	end

	subgraph Transport["Production device transport - planned"]
		Broker[MQTT broker with per-device access controls]
	end

	subgraph Diagnostics["MQTT diagnostics - implemented and API 34 verified"]
		LabBroker[Loopback TLS amqtt synthetic probe broker]
		LabService[HiveMQ client in user-started dataSync foreground service]
	end

	subgraph Device["Managed Android device - basic API 34 kiosk lab verified"]
		Agent[Kotlin Device Owner agent]
		Room[(Room inbox and outbox - planned)]
		Policy[DevicePolicyManager: kiosk controls]
		Boot[Persisted lab kiosk intent and post-unlock boot receiver]
		Installer[PackageInstaller - local lab verified; release delivery planned]
		Sample[Sample managed application]
	end

	Admin --> Console
	LabBroker <-->|localhost:8883 via adb reverse; synthetic probe and ack only| LabService
	Console -->|Authenticated HTTPS actions| API
	API -->|Transactional state, audit, and outbox writes| DB
	DB --> Publisher
	Publisher -->|Command notifications| Broker
	Broker -->|Device-scoped commands| Agent
	Agent <-->|Durable pending work and results| Room
	Agent -->|Apply policy| Policy
	Agent -->|Persist enable or exit| Boot
	Boot -->|Restore enabled intent after boot| Policy
	Policy -->|Lock task mode| Sample
	Agent -->|Verify and install release| Installer
	Installer -->|Update APK| Sample
	Agent -->|Authenticated HTTPS download| Artifacts
	Agent -->|Telemetry and command results| Broker
	Broker --> Ingest
	Ingest -->|Persist before application acknowledgement| DB
	Ingest -->|Durable-ingestion acknowledgements| Broker
	Broker -->|Application acknowledgements| Agent
	Agent <-->|HTTPS reconciliation and event retry| API
	API -->|SSE status refresh| Console
```

### End-to-End Flow

1. An administrator assigns a policy or deployment through the console. The API commits desired state, audit history, and a publish-outbox record in one PostgreSQL transaction.
2. A durable publisher notifies the agent through MQTT. The agent persists the command in Room before acknowledging receipt, then applies supported Android controls or performs the verified update workflow.
3. APKs travel over authenticated HTTPS, not MQTT. The agent verifies signed metadata and APK identity/integrity before installation; observed version and application health determine deployment success.
4. Telemetry and execution results remain in the device outbox until the backend acknowledges durable ingestion. Retries use event IDs and idempotent processing to avoid duplicate history.
5. After missed messages or network loss, HTTPS reconciliation restores convergence with desired state. The console refreshes from backend state through SSE; broker delivery alone never means a command succeeded.

### Boundaries and Reliability

- PostgreSQL owns fleet desired state and durable backend work; Room owns device-local pending work and unacknowledged results.
- The target design uses device mTLS identities and per-device broker permissions. Console authentication and authorization are separate; the browser receives no device or broker credentials.
- Device Owner provisioning is separate from backend enrollment. Only authorized disposable lab devices should be provisioned during feasibility work.
- Android scheduling and power restrictions still apply. MQTT improves responsiveness but does not guarantee continuous background execution; HTTPS recovery is deliberately part of the design.

## Documentation Standard

Documentation is part of the implementation deliverable, not a final cleanup task. Changes to behavior, interfaces, setup, security, or deployment must update the relevant documentation in the same change. Keep this architecture diagram and end-to-end flow aligned with the code, label planned versus verified capabilities, and record validation results and known limitations without overstating them.

## Start Here

- [Project plan](PROJECT_PLAN.md): scope, stack, roadmap, and acceptance gates.
- [Android feasibility lab](docs/android-feasibility.md): prerequisites, build, disposable emulator provisioning, recovery, and remaining gates.
- [Recorded tests](docs/test-evidence.md): screenshot/video replays, captured passing and failed runs, and repeatable commands.

```sh
sh tools/doctor.sh
```

No device is provisioned, reset, or modified by the prerequisite checker.
