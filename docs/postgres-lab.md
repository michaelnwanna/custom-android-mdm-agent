# Private PostgreSQL Lab

This workflow builds PostgreSQL 17.11 into the ignored `.lab-private/postgres-install/17.11` directory. It does not add PostgreSQL to global PATH, install a login service, modify an existing database or use Docker. It is a minimal development build without optional TLS, ICU, GSSAPI, Readline, LZ4 or ZSTD support, not a production deployment recommendation.

## Install

Python 3.12 or later, a C toolchain and `make` are required. Run from the repository root:

```sh
python3 tools/install_postgres_lab.py
```

The [installer](../tools/install_postgres_lab.py) downloads the official source archive from `https://ftp.postgresql.org/pub/source/v17.11/postgresql-17.11.tar.bz2`, checks SHA-256 `dd27f2b3c59e73ed14aa3324901242bf69a032a6347805f274e6260322d42979`, extracts with Python's data-only tar filter and runs configure, `make -j4` and `make install`. An existing installation prefix is not overwritten. `--archive /absolute/location/postgresql-17.11.tar.bz2` reuses an already downloaded archive with the same checksum check. The pin matches Homebrew's PostgreSQL 17.11 formula and the [official checksum file](https://ftp.postgresql.org/pub/source/v17.11/postgresql-17.11.tar.bz2.sha256); it is a consistency check, not a signature or signed provenance guarantee.

Build commands, full logs, a copy of the installer, source URL/checksum and result hashes are retained under `artifacts/postgres-build/<UTC timestamp>/`. Source/build directories and installed executables stay ignored. A failed build remains a failed evidence bundle; never relabel it after a later successful attempt.

On slower hosts, the initial compilation can take substantial time. The installer now waits for build completion without a per-command deadline. `--jobs 1` selects serial compilation; the default is four jobs. To resume a known configured build, supply `--resume-build .lab-private/postgres-build/<original timestamp>/postgresql-17.11` together with `--archive` pointing to the original checksum-matching archive. A resumed build records its source directory and configure options, but is not a pristine rebuild or byte-for-byte reproducibility claim. Never run two builds in the same tree.

The [initial parallel build](../artifacts/postgres-build/20261016T140455.986410Z/manifest.json) hit the former twenty-minute subprocess deadline; that timeout left descendant `make` processes alive. The [first resume](../artifacts/postgres-build/20261016T143303.867748Z/manifest.json) then encountered a missing `lex.backup` scanner-generation artifact while both builds overlapped. The specific orphaned build processes were stopped before a serial resume. Both failures remain failures; this does not establish a PostgreSQL compiler/source defect.

The [serial resume](../artifacts/postgres-build/20261016T143940.919838Z/manifest.json) reached the server link but failed because `utils/misc/guc-file.o` was absent. With no build processes remaining, a [targeted repair](../artifacts/postgres-build/20261016T143940.919838Z/missing-object-repair.log) rebuilt that generated scanner object before another serial resume. The interrupted tree was reused; this is not clean-build evidence.

Homebrew installation was attempted on the Intel macOS host, but its unsupported-platform path required source-building CMake and upgrading several shared libraries. The attempt was interrupted before PostgreSQL installation; DocBook and DocBook XSL dependencies had already been installed. Those unrelated package-manager changes were not reverted. The project-local build avoids further shared dependency upgrades. The [first local configure attempt](../artifacts/postgres-build/20261016T140135.760383Z/manifest.json) failed on an unsupported configure option; its logs remain preserved.

## Run Database Checks

```sh
python3 tools/check_postgres.py
```

The [runner](../tools/check_postgres.py) creates a new private data directory per run and an owner-only temporary Unix-socket directory. `initdb` permits trusted local socket connections, rejects TCP authentication, and the server has an empty `listen_addresses` setting, so no TCP listener is opened. The local superuser is lab-only. Same-user processes can still access the socket; this is not an isolation boundary against a compromised account. No password or external connection is needed. Default PostgreSQL durability settings are retained and recorded, not disabled to accelerate tests.

The runner records PostgreSQL version, configure options, executable hashes, initialization, startup/settings and shutdown, then invokes the existing backend evidence runner with an ephemeral socket connection in the child environment. It does not record that environment or generate bootstrap secrets as fixtures. Backend inputs, hashes, logs, NuGet versions and TRX results are nested in `artifacts/postgres/<UTC timestamp>/backend/`. A passing wrapper requires a passing backend manifest, no remaining test schemas and a stopped server. The ignored data directory remains available for inspection; the temporary socket directory is removed only after shutdown. A killed runner or failed shutdown may require explicit operator cleanup using the recorded data directory; do not delete a running cluster.

To use another installed PostgreSQL toolchain:

```sh
python3 tools/check_postgres.py --bin-dir /absolute/location/postgresql/bin
```

For an independently managed disposable database, set `FLEET_TEST_POSTGRES` directly in your terminal and run `python3 tools/check_backend.py --postgres`. Never pass real credentials in command arguments or chat. The [integration scenario](../backend/tests/FleetManagement.Postgres.Tests/EnrollmentClaimTests.cs) creates a random schema, applies EF migrations, exercises concurrent claims/retries/expiry/rollback and selected database constraints, then drops only its schema. It never silently skips when the connection is missing. Use only an authorized disposable database with schema-creation privileges.

## Verification Limits

The [lab signer run](../artifacts/postgres/20261016T211929.191670Z/replay.html) passed twelve integration tests and ten tooling tests. It adds concrete local-file-ledger signer recovery through new adapter/coordinator/context instances after a forced completion-save failure, preserving the originally issued certificate. This is not a signer-process restart or combined file-ledger/database power-loss test. The existing separate controlled PostgreSQL restart coverage is retained. The wrapper stopped PostgreSQL and found zero test schemas. See [lab signer adapter](../backend/README.md#lab-signer-adapter) for trusted-filesystem and caller-owned-key requirements. References below to absent signing describe earlier milestones; lab-only signing is now implemented, production CA integration is not.

The [signing-coordinator run](../artifacts/postgres/20261016T210746.713204Z/replay.html) passed eleven database integration tests and ten tooling tests. Signer fault cases use an in-memory test double, while attempt fencing and completion recovery use real PostgreSQL transactions. Controlled restart preserves both an unresolved signing marker and completed response. There is no real CA integration, signer-process restart, power-loss or ambiguous-commit validation. See [signing coordination](../backend/README.md#signing-coordination) for the deliberate no-automatic-reissue policy and possible permanently unresolved operations.

The [completion/restart run](../artifacts/postgres/20261016T205603.076038Z/replay.html) passed three integration tests and ten tooling tests, extending controlled restart to the persisted certificate response and its original validity/identity. Completion concurrency, rollback, authorization/expiry and selected constraints are covered; see [durable completion](../backend/README.md#durable-completion). This adds validated response storage, not a signer, revocation policy or HTTP endpoint. The earlier evidence below retains its narrower historical scope.

The [controlled restart run](../artifacts/postgres/20261016T154700.897152Z/replay.html) passed both database scenarios, including `CommittedClaimSurvivesFastPostgresRestart`. That test commits a claim, performs a fast PostgreSQL restart, checks that the postmaster start time changed, then verifies the original operation/device IDs, CSR digest and token expiry. Private networking and durability settings are checked again after restart. The [nested results](../artifacts/postgres/20261016T154700.897152Z/backend/replay.html) include ten passing tooling tests; the [component regression](../artifacts/backend/20261016T155007.896710Z/replay.html) passed forty-six .NET tests and ten tooling tests.

`check_postgres.py` explicitly enables this test only for its marked private cluster and retains the restart log. The test checks the socket, role and actual data directory before restarting. The backend runner requires the named restart test to appear as passed in TRX when `--postgres-restart` is selected. Ordinary `check_backend.py --postgres` excludes restart tests and does not restart an independently managed server. A controlled fast restart is not power loss, an immediate crash, disk corruption or uncertain-signing recovery.

The [successful installation manifest](../artifacts/postgres-build/20261016T145504.904222Z/manifest.json) records PostgreSQL 17.11 after the targeted repair above. The [real-database replay](../artifacts/postgres/20261016T151840.312440Z/replay.html) passed migration, concurrency, context-reopen retry, conflict, rollback, expiry and selected constraint checks, with zero remaining test schemas and successful shutdown. The source build reused the interrupted tree; it is not a clean rebuild or signed binary distribution.

Real database execution must be established by a saved passing PostgreSQL bundle, not by installation or test compilation. Context reopen checks alone are not PostgreSQL restart evidence; the named controlled-restart test establishes only its explicitly recorded scope. None of these checks establish power-loss, disk-corruption or uncertain-signing recovery. The backend now stores validated certificate responses, but does not implement signing, revocation enforcement, HTTP enrollment or production database administration. Existing Android/current-stable acceptance boundaries are unchanged.