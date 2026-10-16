# Phase 1 Development Acceptance

Decision date: 2026-10-16.

Phase 1 is accepted for API 34 development feasibility under the explicitly revised scope in the [project plan](../PROJECT_PLAN.md#10-roadmap-and-exit-gates). Foundation development may proceed. This is a scope decision authorized by the project owner, not a new runtime result or a pass of the original current-stable compatibility requirement.

## Accepted Evidence

| Development gate | Retained verification |
| --- | --- |
| Device Owner and genuine lock task, Home/Recents containment | [API 34 runtime record](android-runtime-verification.md), [restriction smoke](../artifacts/android/20261016T101959.563123Z-smoke/replay.html) |
| Selected restrictions and owned-only cleanup; signed maintenance and negative proofs | [Five-checkpoint maintenance run](../artifacts/android/20261016T110746.710228Z-phase-one/replay.html), [manifest](../artifacts/android/20261016T110746.710228Z-phase-one/manifest.json) |
| Local silent sample update and four APK rejection cases | [Sixteen-checkpoint fresh update run](../artifacts/android/20261016T104623.288748Z-update/replay.html) |
| Persisted enabled/disabled intent and warm-reboot restoration | [Six-checkpoint reboot run](../artifacts/android/20261016T104301.613674Z-reboot/replay.html) |
| Diagnostic TLS messaging, reconnect, process death and explicit restart/stop | [Seven-checkpoint MQTT lifecycle run](../artifacts/android/20261016T104119.390614Z-mqtt-lifecycle/replay.html) |
| Build, lint, tests and media | The maintenance manifest retains build/lint and thirty-one Python/eight JVM test results; the four maintenance/update/reboot/MQTT bundles retain passing native media validation |

The original manual runtime checks were not recorded. Later recorded reruns do not reconstruct the missing original screenshots. All historical failures remain failures; see the [evidence guide](test-evidence.md).

## Deferred Release Gate

Current-stable Android compatibility is deferred to hardening, not waived for broader support or production-readiness claims. Android 17 Device Owner was confirmed, but complete maintenance, kiosk/reboot, update, MQTT, and validated-video coverage remain unverified. Preserve the [failed attempts and recovery experiments](test-evidence.md#android-17-current-stable-compatibility-attempt); do not relabel them as passes.

Resume on a stable supported current-stable runtime. Record exact host/image/API/target/dependency versions, verify ownership and safe recovery first, then complete the applicable recorded suites and native media validation. Claims must remain limited to tested versions and scenarios. No universal API 30+ or OEM support is established.

## Foundation Handoff

Phase 2 starts with versioned contracts and shared acceptance/rejection fixtures, followed by the ASP.NET Core persistence, enrollment/authentication and infrastructure foundations in the plan. Backend services and console are not yet implemented. This acceptance record does not complete Phase 2.

The lab's debug/ADB operator pairing, debug-signed local APK update and anonymous loopback MQTT diagnostic are not production enrollment, release delivery or command transport. Production identity, authorization, revocation, durable synchronization and authenticated updates retain their later gates. Do not restart Docker or wipe any device as a consequence of this scope decision.