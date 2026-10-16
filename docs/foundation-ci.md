# Foundation CI

The [foundation workflow](../.github/workflows/foundation.yml) runs on pushes, pull requests and manual dispatch using Ubuntu 24.04, Python 3.13 and the exact SDK from [global.json](../backend/global.json). Repository permissions are read-only. It runs the retained contract runner and backend component runner, including local signer subprocess tests and compilation of the PostgreSQL test project. Backend checks still run when contract validation fails. A failed check fails the job; no test failure is converted to success.

The final upload step runs even after failure and retains source snapshots, manifests, commands, dependency versions, TRX and HTML replays for thirty days. Generated .NET `bin`/`obj` trees are excluded. Existing checked-in historical evidence is included; identify the new run by its timestamp and manifest rather than assuming every bundled replay passed. Interrupted/cancelled execution may produce incomplete evidence. GitHub artifact retention is not permanent archival; locally published evidence remains in Git. CI does not generate browser screenshots.

No repository secrets or private lab keys are required. Process tests generate their own transient CA keys and pass them through redirected stdin, never the artifact tree. Third-party actions use maintained major-version tags; immutable action SHA pinning and dependency lockfiles are future hardening, not claimed supply-chain guarantees.

## Verification Boundary

Workflow YAML parsing and local test execution do not prove a hosted GitHub run passed. Inspect the actual Actions result after publication. Linux execution is a new platform gate: host-specific failures must remain failed and be investigated, not skipped. No PostgreSQL server, Android emulator, Device Owner runtime, OIDC, production CA or full-system deployment is exercised by this workflow. The private PostgreSQL runner remains a separately recorded local gate, not a CI pass inferred from test-project compilation.

## Remaining MVP Work

The enrollment foundation is not the full portfolio MVP. Remaining acceptance work includes the ASP.NET Core HTTP host with strict body handling, authentication and rate limits; administrator OIDC and authorization; Android Keystore enrollment and mTLS; durable Room inbox/outbox and HTTPS reconciliation; backend event ingestion, audit and transactional outbox; signed resumable APK delivery and installation recovery; the management console; reproducible infrastructure; and end-to-end offline/security/load tests. Current-stable Android compatibility remains deferred and unpassed. The [project plan](../PROJECT_PLAN.md) remains prospective, with no progress status inserted into it.