# Administrator Enrollment Provisioning

`POST /api/v1/admin/pending-enrollments` creates a server-assigned pending enrollment and delivers its bootstrap token to an authorized operator. It is separate from device bootstrap redemption. See the [OpenAPI contract](../contracts/openapi.json) and [enrollment host configuration](enrollment-http.md).

## Identity Configuration

Configure `Administrator__Authority` with the trusted HTTPS OIDC authority and `Administrator__Audience` with a dedicated API audience. Configure the identity provider to issue RFC 9068-style access tokens with `typ=at+jwt`, RS256 or ES256 signatures, an expiry, issuer, subject, and an exact `permission` claim value of `enrollments:create`. Claim-name mapping is disabled. Tokens for another audience, ID tokens, opaque bootstrap tokens and merely having an administrator-like role do not grant this permission. JWT validation uses the maintained ASP.NET Core bearer handler and HTTPS discovery/signing-key metadata; keys are not supplied in requests. Clock skew is thirty seconds. No authority/audience configuration leaves the route unable to authenticate; partial or unsafe configuration fails startup.

The API does not implement browser OIDC login, authorization-code/PKCE, identity-provider administration, role assignment or a management console. An operator must obtain the correct API access token through their organization's separately secured identity-provider flow. No default operator, static bypass key, password, issuer or signing key is bundled. Tests use ephemeral signing keys and explicit validation configuration, not a live identity provider; discovery availability, key rollover and a real operator login remain deployment gates.

## Request and Delivery

The authenticated request body is:

```json
{"schemaVersion":1,"lifetimeSeconds":300}
```

Lifetime is an integer from sixty through nine hundred seconds. Client-selected IDs, duplicate/unknown fields and unsupported versions are rejected. Only unencoded UTF-8 JSON is accepted; the body limit is 1024 bytes, including streams without Content-Length. Requests require HTTPS and an Authorization bearer header. There is no cookie authentication, so a browser session cookie alone cannot create enrollments. No permissive CORS policy is enabled.

The process-wide creation budget is ten requests per minute, including unauthorized attempts, separate from the redemption budget. There is no distributed or per-operator limiter. Operations use a fifteen-second cooperative cancellation deadline. Apply network admission and distributed abuse controls before production exposure.

A 201 JSON response contains `schemaVersion`, `pendingEnrollmentId`, `bootstrapToken`, `issuedAt` and `expiresAt`, with `Cache-Control: no-store`. The token is sensitive: do not put it in URLs, logs, issue trackers, retained test evidence, clipboard history or analytics. Transfer it to the intended managed device through a separately authorized confidential channel. The [Android lab client](android-enrollment.md) has combined API 34/HTTPS/PostgreSQL verification using ephemeral test-issued administrator credentials and trusted private ADB staging. Android scanning, live identity-provider login and secure production token delivery remain unverified. In-memory strings and the operator's receiving client cannot be guaranteed zeroizable.

The service saves only the keyed token digest plus the pending identity/lifetime. In the same EF transaction it records the verified administrator issuer and subject, pending ID and creation time in `EnrollmentProvisioningAudits`. A failed audit insert rolls back creation. The response is constructed only after saving succeeds. The audit is a minimal provisioning attribution record, not an immutable/tamper-evident audit system or full security-event history; database administrators can still change it. It contains no access token or bootstrap secret. Existing library-created test enrollments need not have an audit row; all new HTTP-created enrollments do.

Creation is deliberately not idempotent and has no secret-retrieval route. A lost response or uncertain commit can leave an unused pending enrollment until its original expiry. Retrying creation creates a different pending enrollment and does not recover or rotate the original secret. Do not automatically retry this operation assuming a response was not committed; verify operator intent and expiry. Cancellation and generic 500/503 responses do not prove rollback after uncertain delivery. Revocation, cleanup, listing and recovery/operator reconciliation are future work.

## Responses and Tests

Missing/invalid access tokens receive generic 401 plus a Bearer challenge; an authenticated token without the permission or required actor identity receives 403. Input/media/body errors use 400/415/413; throttling uses 429; an unconfigured provisioner uses 503; persistence errors are generic and may use 500 or 503. Application responses use no-store; errors contain no credential or database details. Retry-After is included for 429/503 but does not make creation idempotent.

Signed-token tests cover issuer, audience, signature, expiry, token type, permission, subject, missing/unconfigured credentials and invalid configuration. HTTP tests cover secret delivery only on success, redacted formatting, denial before provisioning, strict bodies, size limits, rate limits, disabled service and failure redaction. The real PostgreSQL HTTP scenario verifies digest-only storage, exact actor attribution after context reopen, denial without mutations and forced audit-insert failure with complete transaction rollback. It uses TestServer rather than a combined network TLS/identity-provider/database deployment. See [recorded evidence](test-evidence.md).