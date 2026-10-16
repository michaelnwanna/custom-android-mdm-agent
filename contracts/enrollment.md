# Enrollment HTTP Contract

Implemented: [OpenAPI 3.1 document](openapi.json), bounded request/response/problem schemas, Python contract checks and [.NET CSR/token/claim components](../backend/README.md), including an EF Core migration and row-locked PostgreSQL claim implementation. Component/model checks and a real PostgreSQL 17.11 migration/concurrency/retry/rollback/expiry/constraint scenario pass. Not implemented: an HTTP server, administrator token-delivery route, certificate issuance/completion, response recovery, mTLS enforcement or signer crash reconciliation. Operational HTTP/issuance rules below remain requirements, not verified server behavior.

## Bootstrap and Identity

OS Device Owner provisioning is separate from backend enrollment. An authorized administrator provisions a pending enrollment and a short-lived one-use opaque token. The Android agent generates a P-256 key in Android Keystore and signs a PKCS#10 CSR with SHA-256; private material never leaves the device. The existing lab maintenance key is not a device-enrollment key.

The client submits `POST /api/v1/enrollments` over server-authenticated TLS with `Authorization: Bearer <bootstrap token>`, `Content-Type: application/json`, and the [request](http/enrollment-request-v1.schema.json). No client certificate exists yet, so this route explicitly overrides the planned device-mTLS requirement. Do not use permissive TLS callbacks or trust roots delivered by this response. Bootstrap trust is provisioned separately by an authorized operator.

The token is bound to `pendingEnrollmentId`, expires at its original deadline and is stored only as a keyed digest; digest keys and signing keys remain outside Git. The .NET component generates 256-bit opaque tokens and enrollment-bound HMAC-SHA-256 digests, accepts a caller-selected positive lifetime up to fifteen minutes, and verifies the original deadline. The claim component persists keyed credentials in PostgreSQL. Secure key provisioning/rotation, administrator/OIDC routes, HTTP authentication and rate-limit storage remain backend work. Verification alone neither consumes a token nor enforces one-use issuance. Do not log authorization headers, CSR bodies, certificate responses or tokens; even rejected requests must be redacted.

## Validation Before Issuance

Enforce the 24576-byte body limit before parsing. Reject duplicate JSON keys, unsupported versions, unknown fields and non-JSON content. `csrDer` is standard base64 encoding of DER, bounded to 16384 encoded characters. Decode strictly, check canonical base64, parse exactly one PKCS#10 object with no trailing data, verify proof of possession and enforce P-256/SHA-256 key/signature policy using maintained cryptographic APIs. Never treat a schema-valid string as a verified CSR.

The shared fixture CSR/certificate strings are deliberately synthetic encoding-shape examples, not real cryptographic material. The schema size tests also use synthetic strings. Separate [.NET component tests](../backend/tests/FleetManagement.Tests/EnrollmentCsrVerifierTests.cs) generate real signed requests in memory and cover encoding, proof-of-possession and algorithm rejection. The verifier returns public SPKI and the digest of exact decoded CSR bytes without treating subject/SAN as authority. Kotlin/.NET JSON contract consumers, Android enrollment and certificate validation remain pending.

The server assigns the immutable UUID identity. CSR subject/SAN data must not grant authority or choose the certificate identity. Certificate issuance constructs a server-controlled device identity, client-auth usage and bounded validity. Clients verify the returned certificate chains to preconfigured trust, matches their generated public key and assigned identity, has valid usage and validity, and agrees with response metadata before using it. The [response](http/enrollment-response-v1.schema.json) includes the leaf and up to four distinct intermediates, never a trust-root update or private key. Root-signed leaf chains may have an empty intermediate array. Subsequent HTTPS/MQTT uses mTLS with authorization and revocation checks; certificate presentation alone is not authorization.

## Durable One-Use Semantics

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Issuing: valid token and verified CSR, exclusive durable claim
    Issuing --> Completed: persist fixed identity, certificate and response
    Issuing --> Issuing: recover same operation, no new identity
    Completed --> Completed: exact CSR retry returns original response
    Pending --> Expired: original token deadline
    Completed --> RecoveryRequired: revoked identity or key loss
```

One-use means one issuance operation, not one successful network delivery. New completion returns `201`; an exact decoded-CSR-byte retry using the original still-valid token returns `200` with the same persisted device ID, certificate and timestamps. It must neither renew validity nor create another device. Bind a cryptographic digest of decoded CSR bytes to the pending enrollment; an authenticated different-CSR retry returns `409`. Once the original token expires, even response recovery returns generic `401`. Re-enrollment or key loss requires a separate authorized workflow, not automatic replacement.

The backend must serialize concurrent redemption and preserve the operation identity across issuer/database/network crashes. External certificate signing is not automatically atomic with PostgreSQL: persist an exclusive issuance claim and fixed identity, then reconcile uncertain signing using a durable operation identifier or persisted issuance material. Never recover by blindly generating another identity or certificate. Persist completion and safe audit state before sending success. The implemented claim component covers the pending-to-issuing transaction only, with in-progress same-CSR retries; it does not yet sign certificates or persist/return a completed response. Its [real-database scenario passed](../artifacts/postgres/20261016T151840.312440Z/replay.html), including eight competing claims and context-reopen retry. This is not process-restart, power-loss or uncertain-signing recovery evidence. Complete issuer/storage behavior requires further concurrency and crash-recovery tests; this contract alone proves none of it.

## Response Rules

Every response uses `Cache-Control: no-store`. Errors use the bounded [problem shape](http/problem-v1.schema.json), `about:blank`, a generic status title and optional opaque trace ID, without raw exception, token, CSR or account details. `status` must equal the actual HTTP status; schemas do not prove that agreement. Missing/expired/invalid/wrongly-bound tokens share generic `401` with `WWW-Authenticate: Bearer`, without disclosing pending-enrollment existence. Authenticate before exposing a conflict.

Other declared outcomes are `400` for malformed/invalid input, `409` for authenticated conflicts/recovery, `413` for body size, `415` for content type, `429` for rate limiting, and `500`/`503` for server failure/unavailable durable completion. `429` and `503` include bounded seconds in `Retry-After`; clients use bounded jittered backoff and retry the same CSR, never a fresh key to evade uncertain state. Rate-limit before expensive verification/issuance. Proxy-level malformed requests or TLS failures may terminate before application problem responses; test that boundary separately.

## Verification Scope

The [recorded runner](../tools/check_contracts.py) validates the OpenAPI structure and local references, checks declared bootstrap/mTLS separation and HTTP metadata, and applies shared schema fixtures and size-boundary tests. It does not send HTTP requests, inspect certificates or enforce any server behavior. Build/runtime integration tests must establish replay, expiry, concurrency, authorization, redaction, rate limits, durable issuance and certificate matching before enrollment can be called functional.