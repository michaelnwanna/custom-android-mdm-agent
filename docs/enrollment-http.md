# Enrollment HTTP Lab

The [ASP.NET Core host](../backend/src/FleetManagement.Api/FleetApi.cs) implements `POST /api/v1/enrollments` using the existing PostgreSQL claims, certificate verifier, signing coordinator and private file-ledger lab signer. This is an enrollment lab, not a complete fleet-management service or production CA.

## Request Boundary

The endpoint requires server-authenticated HTTPS and a single bootstrap bearer token. Cleartext requests are rejected without redirect. Only JSON with absent or UTF-8 charset is accepted; content encodings are rejected. A bounded reader enforces 24576 bytes even without Content-Length. The decoder rejects duplicate keys (including escaped names), unknown fields, unsupported versions, invalid UUIDs and noncanonical base64 before redemption. Cryptographic CSR verification occurs after token authentication in the claims transaction.

The endpoint has a process-local, endpoint-wide fixed-window limit of sixty requests per minute, without a queue, including unauthorized requests. It is not a per-device or distributed abuse-control system; a caller can exhaust the shared budget. A fifteen-second cancellation deadline covers body reading and redemption, but cancellation is cooperative and cannot undo committed claims or completed signing. No forwarded-header trust is configured: terminate TLS at Kestrel for this lab rather than assuming a proxy makes cleartext upstream requests secure.

Successful new claims return 201; authenticated exact-CSR recovery returns 200 with the same serialized certificate response. Expiry and invalid credentials return generic 401, authenticated CSR conflicts return 409, invalid requests return 400, oversized bodies return 413, unsupported media types return 415, and rate limiting returns 429. Unavailable or uncertain completion returns 503; unexpected failures return generic 500. Application responses use no-store, failures use problem JSON, 401 includes a Bearer challenge, and 429/503 include Retry-After. TLS and Kestrel protocol rejection may happen before application error handling.

## Configuration

Run from the backend directory using the pinned SDK:

```sh
dotnet run --project src/FleetManagement.Api/FleetManagement.Api.csproj --no-launch-profile
```

Configure Kestrel's HTTPS endpoint and server certificate through its standard configuration, separately from the enrollment-signing CA. Do not disable client server-certificate validation. Do not commit passwords, connection strings or keys; use operator-controlled environment configuration or an external secret provider.

Set all five enrollment settings, using double underscores for environment variables:

| Setting | Required Value |
| --- | --- |
| `Enrollment__ConnectionString` | Authorized PostgreSQL connection with the existing migrations applied |
| `Enrollment__DigestKeyFile` | Absolute path to exactly 32 cryptographically random binary bytes |
| `Enrollment__CaCertificateFile` | Absolute path to the dedicated lab P-256 CA PEM certificate |
| `Enrollment__CaKeyFile` | Absolute path to its private PEM key |
| `Enrollment__LedgerDirectory` | Absolute path to a private trusted local ledger directory |

Keep all file material under ignored `.lab-private` or outside the repository with owner-only access. Reuse the digest key and CA/ledger for recovery; replacing them is not a safe retry mechanism. The host does not migrate the database, generate these secrets, create pending enrollments or deliver bootstrap tokens. A trusted caller must provision pending enrollments through the existing library; there is no administrator HTTP route or automatic development credential. With no enrollment configuration the endpoint returns 503; partial configuration fails startup. Kestrel must also have a valid server TLS configuration before meaningful use.

The configured lab signer issues one-day certificates; the completion verifier allows at most two days. Root ownership, clock, file permissions, backups and trusted filesystem integrity remain operator responsibilities. The ledger is not protected against rollback, deletion, power loss or untrusted local administrators. Request-body/header logging must remain disabled; configure any external proxy or telemetry sink to redact credentials and enrollment material too.

## Verification and Limits

Component tests cover strict decoding, pipeline response mappings, no-store and generic errors, declared and undeclared body lengths, rate limiting, unconfigured behavior, and real loopback Kestrel HTTPS with trusted and untrusted clients. The real TLS test uses a stub redemption service; it is not the same invocation as the database-backed HTTP test. The latter uses TestServer with real PostgreSQL and the lab signer to verify issuance, competing retries, byte-identical response replay after service recreation, conflict and expiry.

Use the [evidence runners](test-evidence.md) for retained component and PostgreSQL results. Neither suite establishes Android Keystore enrollment, device mTLS, administrator OIDC, revocation, production CA transport, public deployment, distributed rate limits or combined TLS/database/device end-to-end behavior. See [remaining MVP work](foundation-ci.md#remaining-mvp-work).