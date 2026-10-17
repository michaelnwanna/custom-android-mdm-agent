# Android Enrollment Lab

Current follow-up: the [device HTTPS mTLS lab](device-mtls.md) verifies an enrolled Keystore identity against Kestrel/PostgreSQL and rejects subsequent access after persisted disable. The older mTLS exclusions below describe enrollment-only evidence. New keys authorize Conscrypt's prehashed TLS signing; older SHA256-only keys fail closed without replacement. MQTT and full credential lifecycle remain outside this proof.

The agent has a manually initiated enrollment client: a non-exportable AndroidKeyStore P-256 signing key, PKCS#10 CSR, exact-request retry journal, pinned HTTPS transport, strict response decoding and pinned certificate validation. It is separate from the existing maintenance key and MQTT diagnostic. Combined Android-to-ASP.NET/PostgreSQL enrollment is verified in the scoped API 34 harness below, not as a production deployment or functional device mTLS.

## Trusted Lab Setup

The Device Owner operator must provision three private app files through an authorized lab channel:

| App-private File | Contents |
| --- | --- |
| `enrollment-server-ca.pem` | One independently provisioned CA certificate trusted for the HTTPS server |
| `enrollment-device-ca.pem` | One independently provisioned root trusted for issued device certificates |
| `enrollment-bootstrap.json` | The bounded bootstrap input described below |

The input shape is distinct from the administrator API response: the operator adds the endpoint from trusted deployment configuration, not from an untrusted certificate response. Its `schemaVersion` is 1, `endpoint` is an absolute HTTPS URL ending exactly in `/api/v1/enrollments`, `pendingEnrollmentId` is the administrator-created UUID, and `bootstrapToken` is the sensitive 43-character canonical base64url token. Query strings, fragments and URL credentials are forbidden. The endpoint hostname must match the server certificate. Do not put real tokens in commands, source control, retained evidence, logs or screenshots. App-private staging through debug/ADB is trusted-lab only, not production secure delivery.

After staging, select **Enroll staged lab identity** in the Device Owner agent. The operation runs on a single background executor, not the main thread. There is no automatic retry, WorkManager scheduler or exported enrollment receiver. Roots must be present even for certificate restoration. The app does not accept a new trust root from the server or download intermediates. No trust-all or permissive hostname-verifier callback is installed.

## Keys and Recovery

Before network I/O the agent saves the exact signed CSR, pending ID, endpoint and both trust-root fingerprints in an AtomicFile journal under `noBackupFilesDir`. It never exports or stores the private key in that file. The fixed key alias is `fleet-enrollment-device`; new generation uses AndroidKeyStore with P-256 and SHA256withECDSA and checks that private-key encoding is unavailable. This does not establish hardware-backed/StrongBox storage, attestation or protection against a privileged device administrator.

An existing prepared identity requires the same pending ID, endpoint and roots. Retries reuse the original CSR bytes and key. Missing keys, a key without its journal, or changed bindings fail without generating a replacement. Do not delete the journal or key as a retry mechanism; authorized operator recovery and key rotation are not implemented.

The staged token file is deleted after successful preparation and before sending the request. The secret is then held only in memory for that attempt. On uncertain network failure, restage the original still-live token and original configuration to retry the same CSR. No new key is created to evade an uncertain issuance. If preparation fails before token consumption, the staged private file can remain for the operator to inspect/remove securely. Unlinking is not guaranteed physical erasure and managed secret strings cannot be reliably zeroized. The server still enforces its original token deadline and exact-CSR recovery rules.

After a validated response, the agent journals the public response before updating the certificate chain on the existing Keystore private-key entry. A later explicit enrollment action revalidates and reapplies that response, allowing recovery if certificate installation was interrupted. This is not a two-resource atomic commit or a power-loss guarantee. A completed journal cannot be replaced by a different response, even one validly signed for the same key. A different binding requires a separate authorized lifecycle workflow, not automatic re-enrollment.

## Validation and Transport

CSR construction uses Bouncy Castle's maintained PKCS#10 API with Android's signature engine and a non-exported private-key handle. Moshi streaming JSON parsing rejects duplicate keys (including escaped names), unknown fields, wrong types, missing fields, unsupported versions and trailing documents. Certificate response size is capped at 100000 bytes both with Content-Length and with chunked/unknown-length bodies. Only 200/201 JSON responses are accepted; redirects are never followed. Connect and read timeouts are finite, but there is no total wall-clock response deadline or background retry policy yet.

Certificates must be canonical single-DER X.509 objects, with no duplicate entries or returned root and at most four supplied intermediates. Validation requires the existing P-256 public key, SHA256/ECDSA leaf signature, non-CA leaf, digitalSignature-only key usage, clientAuth-only EKU, exactly one URI SAN matching the server-assigned nonzero device UUID, exact response/certificate validity metadata, current validity and a two-day maximum lifetime. PKIX builds only from supplied ordered certificates and the separately configured root. Revocation is disabled; revocation and disabled-device enforcement remain server/client lifecycle work. CSR subject and SAN requests are not identity authority; the server assigns identity and the client checks it against the returned signed certificate.

## Verification

### Combined Backend Runner

The [successful replay](../artifacts/android-backend/20261017T071554.735623Z/replay.html) and [manifest](../artifacts/android-backend/20261017T071554.735623Z/manifest.json) record twenty passing JVM tests, APK build/lint, one real-backend instrumentation scenario, independent backend public-proof/database verification and clean database shutdown. Earlier readiness, response-field and certificate-time failures remain retained as failed snapshots.

Integration exposed a wire mismatch: responses use the contract's `issuedAt` and `expiresAt`, mapped to internal certificate validity fields; `notBefore`/`notAfter` wire names are rejected. The lab signer backdates validity by thirty seconds to tolerate the observed two-second device clock lag, without increasing total certificate lifetime. Strict client not-before/expiry checks remain unchanged. This bounded lab allowance is not clock synchronization or arbitrary skew tolerance; clients outside the validity interval still fail.

The test-only combined runner requires the existing private PostgreSQL 17.11 installation and an already booted, explicitly authorized disposable AVD:

```sh
python3 tools/check_android_backend.py --serial emulator-5554 --expected-avd Pixel_8
```

It snapshots Android/backend/test-host inputs, builds both clients, runs the twenty JVM tests and lint, installs debug APKs without clearing app data, starts a private Unix-socket-only PostgreSQL cluster and runs the actual API through loopback-bound Kestrel HTTPS. The standard emulator host alias `10.0.2.2` reaches that listener; the ephemeral TLS certificate explicitly contains that IP SAN. Device certificates use a different ephemeral CA. No host-wide trust store is changed and no production/test-backdoor API is added.

The companion host uses an ephemeral test-issued API access token to call the existing authorized administrator provisioning route. It is not a live identity-provider login. The bootstrap token passes only through process pipes and a private target-app staging file, never command arguments or retained configuration. The runner refuses preexisting integration staging files. Instrumentation deletes its input, uses random test-only Keystore aliases, exercises the production transport/identity classes, compares exact response bytes on retry, rejects a wrong token and a conflicting CSR, then exports only public certificate/CSR/signature proof. The host independently verifies the signed proof, certificate, single persisted completion, provisioning audit and one issued ledger result. The runner removes its staging files and shuts down its own database; instrumentation removes its own aliases/journals. The normal `fleet-enrollment-device` identity is not used or reset.

Evidence goes under `artifacts/android-backend`; a failed result remains failed even if some steps succeeded. An emulator boot-readiness failure is not enrollment execution. This harness does not establish the UI-button workflow, live OIDC, production token provisioning, mTLS, power-loss safety or process/reboot recovery. The combined run above is separate from earlier isolated evidence below; older statements about unverified combined redemption describe those older runs only.

Cleanup is best effort after abnormal process/device termination: a hard crash can leave test aliases or private staging behind. The runner refuses preexisting staging instead of deleting unknown inputs; operators must inspect leftovers before another run. File deletion is unlinking, not guaranteed secure erasure, and managed secret strings cannot be reliably zeroized. The companion host emits private configuration only through the runner-owned stdout pipe; do not run it standalone under a console logger. No private stdout is copied into the public manifest.

The final [runner revision replay](../artifacts/android-enrollment/20261016T234945.470559Z/replay.html) passed all twenty JVM tests, build/lint and the isolated API 34 scenario. Generated APK fingerprints are recorded separately from source fingerprints for both local runtime and build-only CI runs. Fifty repository Python tooling tests passed in the existing contract-validator environment.

Run snapshot-based build/JVM verification from the repository root:

```sh
python3 tools/check_android_enrollment.py
```

For an already booted, authorized disposable emulator only:

```sh
python3 tools/check_android_enrollment.py --serial emulator-5554 --expected-avd Pixel_8
```

The runner never boots, wipes, provisions or uninstalls a device. It checks the explicit AVD name, boot readiness and shared storage before installing the debug agent and test APK with replacement enabled. This updates the lab agent while retaining its app data and Device Owner identity; run only on the intended disposable lab. Instrumentation creates unique test aliases/directories and removes only its own test material. It requires explicit test-success output in addition to the instrumentation result code. Evidence snapshots, dependency reports, build/lint/JUnit results, APK fingerprints, device fingerprint and raw commands are retained under `artifacts/android-enrollment`.

The [recorded API 34 run](../artifacts/android-enrollment/20261016T234047.043302Z/replay.html) passed twenty JVM tests (eight existing maintenance plus twelve enrollment), APK assembly/lint and one isolated AndroidKeyStore instrumentation scenario. That scenario verifies non-exportability, exact CSR after object recreation, binding rejection, certificate installation/restoration, signing with the same key, immutable completed identity and refusal to recreate a missing key. It uses an in-memory test CA, not the backend. JVM loopback TLS tests separately cover trusted server success, wrong roots/hostnames, redirects and oversized responses. No UI enrollment button flow, combined backend redemption, real process reboot/restart, power loss, mTLS request, hardware attestation, production token transfer, API 37 or physical-device enrollment has been verified. The [first evidence-runner failure](../artifacts/android-enrollment/20261016T234023.179850Z/replay.html) remains failed; it lost wrapper executable permission while copying inputs, corrected by preserving source metadata.