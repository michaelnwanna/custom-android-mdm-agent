# Phase 2: Policy Delivery and Application Updates

This document outlines the next major work after the foundation (Phase 1) is complete. Phase 1 has verified:
- Secure HTTPS enrollment
- Device mTLS authentication
- Durable event ingestion with PostgreSQL
- WorkManager-based event delivery retry

## Phase 2 Objectives

### 2.1 Policy Delivery Workflow

**Goal**: Administrators can create policies, assign them to devices, and verify delivery.

#### Backend Implementation

1. **Policy Model**
   - Immutable policy ID (UUID from JSON hash for idempotency)
   - Versioned content (version increments on updates)
   - Creator audit trail
   - JSON payload (compact, validated at creation)
   - Active/inactive flag (only active policies are delivered)

2. **Policy Assignment Model**
   - Device ↔ Policy many-to-one relationship
   - Assigned version (snapshot of policy version at assignment time)
   - Acknowledged version (tracked from device events)
   - Assignment timestamp and last-reported timestamp

3. **API Endpoints** (Require `policies:create` and `policies:assign` permissions)
   ```
   POST /api/v1/policies/create
   POST /api/v1/policies/assign
   GET /api/v1/policies/{policyId}
   ```

4. **Database Migrations**
   - Create `policies` table with indexes on `PolicyId`, `IsActive`, `CreatedAt`
   - Create `policy_assignments` table with FK to both `policies` and `enrolled_devices`
   - Audit logging for policy creation and assignment

#### Android Implementation

1. **Command Decoder Enhancement**
   - Already supports `SET_POLICY` command type
   - Validates `policyId` and `policyVersion`
   - Persists command in Room inbox

2. **Policy Application Handler**
   - Receives SET_POLICY command
   - Loads device restrictions from command payload
   - Applies restrictions using `DevicePolicyManager`
   - Reports success/failure via event

3. **Policy State Persistence**
   - Room database tracks current policy ID and version
   - Survives device reboot and app restart
   - Used for reconciliation after offline period

#### Acceptance Criteria

- ✅ Create policy with JSON validation and size bounds (64 KB)
- ✅ Idempotent creation: same JSON → same UUID
- ✅ Assign policy to enrolled device
- ✅ Verify backend generates SET_POLICY command
- ✅ Android receives and applies policy via command handler
- ✅ Device reports policy application success/failure
- ✅ Backend tracks acknowledged version

### 2.2 Application Update Pipeline

**Goal**: Deliver signed application packages with resumable downloads and installation tracking.

#### Release Management

1. **Release Model**
   - Immutable release ID (UUID from metadata hash)
   - Package name, version code, APK hash, size
   - Signed release metadata (detached signature over structured bytes)
   - Minimum SDK requirement
   - Expiry timestamp
   - Artifact object ID (reference to immutable APK storage)

2. **Signed Metadata Format**
   - JSON document with fields: `releaseId`, `packageName`, `versionCode`, `sha256`, `bytes`, `minSdk`, `expiresAt`
   - Exact byte-level signing (no whitespace normalization)
   - ECDP-256 SHA-256 signature (or similar vetted algorithm)
   - Public key embedded in app via build configuration

3. **Storage Boundary**
   - Immutable artifact storage (local HTTP with Range/ETag support)
   - APK bytes stored separately from metadata
   - Future S3-compatible storage interface

#### Android Implementation

1. **APK Download**
   - Receive INSTALL_RELEASE command with release ID
   - Fetch signed metadata from backend over HTTPS/mTLS
   - Verify signature using embedded public key
   - Validate release expiry, min SDK, package name
   - Download APK using Range requests for resumability
   - Persist download state and ETag across reconnects

2. **Installation Tracking**
   - Use `PackageInstaller` session API
   - Persist session ID across crashes
   - Handle `STATUS_PENDING_USER_ACTION` and other terminal states
   - Report installation success/failure with observed version code
   - Reconcile actual installed package after process death

3. **Deployment State**
   - Track piloting, staged rollout, and promoted releases
   - Room table: `release_deployments` with `releaseId`, `stage`, `progress`, `errors`
   - Device reports observed version; backend tracks deployment progress

#### Acceptance Criteria

- ✅ Create signed release metadata
- ✅ Verify signature with embedded public key
- ✅ Download APK with Range requests and resumability
- ✅ Persist download state across reconnects
- ✅ Install via PackageInstaller
- ✅ Report installation outcome (success/failure with version)
- ✅ Backend tracks deployment progress
- ✅ Pilot and promotion workflow

### 2.3 Management Console

**Goal**: Provide an operations interface for fleet management.

#### Stack
- React + TypeScript
- Vite for bundling
- TanStack Query for server state
- OpenAPI code generation for type-safe API calls
- OAuth 2 / OIDC for administrator login

#### Pages

1. **Login** (OAuth 2.0 / OIDC)
   - Redirect to configured OIDC provider
   - Exchange code for token
   - Store token in secure HttpOnly cookie

2. **Fleet Dashboard**
   - Device count, health summary
   - Last reconciliation, average queue depth
   - Critical events (failures, disconnects, errors)
   - Filters: site/group tags, policy version, application version

3. **Device Detail**
   - Enrolled certificate, device ID, serial
   - Current policy (version and applied settings)
   - Application versions (observed vs. desired)
   - Telemetry (battery, storage, connectivity)
   - Command history (last 20 commands and results)
   - Events (last 50 with timestamps and state)

4. **Policy Management**
   - Create new policy (JSON editor with schema validation)
   - List policies and versions
   - Assign policy to devices
   - View assignment progress (count assigned, acknowledged, failed)

5. **Release Deployment**
   - Create release (upload APK, set metadata)
   - Create deployment (select release, pilot devices, staged rollout)
   - Track progress (devices: pending, downloading, installing, completed, failed)
   - Promote from pilot to broader rollout

6. **Audit History**
   - Policy changes
   - Release deployments
   - Administrator actions
   - Device lifecycle (enrollment, disable, re-enrollment)

#### Authentication & Authorization

- OIDC token in Authorization header or secure cookie
- Backend validates token audience and issuer
- Permission claims: `policies:read`, `policies:create`, `devices:read`, `deployments:create`, etc.
- Role-based access control (Administrator, Viewer)

#### API Requirements

- OpenAPI 3.1 schemas for all endpoints
- Consistent error responses
- Server-Sent Events (SSE) for live updates (optional Phase 2.5)

### 2.4 Telemetry Collection

**Goal**: Gather device health metrics and application/policy versions.

#### Telemetry Events

1. **Device State** (every ~15 minutes or on demand)
   - Battery level, charging state, temperature
   - Free/total storage (device and app-private)
   - Network state (connected, type, signal strength)
   - Screen state (on/off)

2. **Application State**
   - Installed application versions (observed)
   - Target application state (from policy)
   - Application process state (running, crashed, stopped)

3. **Policy State**
   - Current policy ID and version
   - Acknowledged policy version (when received)
   - Policy application success/failure reason

4. **Queue State**
   - Pending command count
   - Pending event count
   - Oldest pending event timestamp

#### Android Implementation

- Room table: `telemetry_events` with type, timestamp, JSON payload
- Bounded storage: 10 MB max, 7 days retention
- Coalesce identical readings; drop oldest samples if full
- Report drop counts

#### Backend Implementation

- Persist events in PostgreSQL
- Calculate device health score (freshness, queue depth, failures)
- Serve via GET /api/v1/devices/{deviceId}/telemetry

#### Acceptance Criteria

- ✅ Collect and bound telemetry on device
- ✅ Upload telemetry via HTTPS/mTLS event endpoint
- ✅ Backend ingests and persists telemetry
- ✅ Console displays device health
- ✅ Query API for device metrics

### 2.5 Offline Synchronization & Reconciliation

**Goal**: Ensure device state converges after reconnecting.

#### Reconciliation Strategy

1. **Device Reconciliation Request**
   - Device periodically (or on reconnect) sends REQUEST_SYNC command
   - Includes observed device state (policies, applications, queue status)

2. **Server-Side Reconciliation**
   - Compare desired state (from assignments) with observed state
   - Identify missing or superseded commands
   - Emit new commands if needed
   - Return summary of expected state

3. **Conflict Resolution**
   - Newer policy versions override older
   - Newer deployments override older
   - Device always applies the highest version it receives

#### Room Outbox Enhancements

- Persist telemetry samples with age-based pruning
- Mark events as retryable or terminal
- Support application-level acknowledgement batching

#### Acceptance Criteria

- ✅ Device sends REQUEST_SYNC on reconnect
- ✅ Server returns current desired state
- ✅ Device applies missing commands
- ✅ Verify convergence after offline period

## Implementation Order

1. **Week 1-2**: Policy Delivery (backend model, endpoints, Android handler, tests)
2. **Week 2-3**: Application Updates (release model, signed metadata, APK download, installation)
3. **Week 3-4**: Management Console (login, device list, policy/release management)
4. **Week 4-5**: Telemetry Collection (device metrics, health dashboard)
5. **Week 5-6**: Offline Reconciliation (REQUEST_SYNC, state convergence, boundary tests)

## Testing Strategy

- **Unit Tests**: Policy versioning, release metadata signing, telemetry sampling
- **Integration Tests**: Policy → device command → application (with real PostgreSQL)
- **API Tests**: Policy creation, assignment, deployment endpoints (with real Android emulator)
- **E2E Tests**: Administrator creates policy → device receives → applies → reports (once console is ready)

## Known Constraints

- WorkManager: ~15-minute minimum for periodic tasks (immediate reconciliation must use MQTT or HTTPS polling)
- Android Background Execution: Power-save modes may delay WorkManager; use connected-network constraint
- Room Database: Bounded storage means telemetry pruning is necessary
- OIDC Integration: Depends on externally hosted provider (not built-in to MVP)

## Deferred to Phase 3

- Real production CA integration (using lab self-signed for Phase 2)
- JobScheduler autonomous boot dispatch
- Zero-touch enrollment and managed Google Play
- Arbitrary remote command execution
- Kubernetes deployment and high-availability setup
- Performance load testing (100+ devices)

---

## Phase 2.1 Progress

### ✅ Completed (Commit b51cdaf – Policy Foundation)

**Backend Foundation**
- [x] Policy domain models (`Policy`, `PolicyAssignment`, `DevicePolicyState`)
  - Policy: immutable, versioned, JSON-based (max 64 KB)
  - PolicyAssignment: tracks device assignment and acknowledgement state
- [x] PolicyService with idempotent creation
  - Same PolicyJson → same PolicyId (MD5 hash-derived UUID)
  - Prevents duplicate policies in database
- [x] REST API endpoints
  - `POST /api/v1/policies/create`: Create or retrieve policy
  - `POST /api/v1/policies/assign`: Assign policy to enrolled device
- [x] Database migration (20261018100421_AddPolicies)
  - `Policies` table with constraints and indexes
  - `PolicyAssignments` table with FK relationships
  - Cascading restrictions on policy/enrollment deletion
- [x] Unit test suite (26 tests, all passing)
  - Model creation and validation
  - Idempotency verification (same JSON → same ID)
  - JSON validation (empty, malformed, oversized, non-object)
  - Service layer with in-memory DB
  - Assignment creation and error cases

**Verification**
- All unit tests passing: 26/26
- Backend builds successfully
- Database schema supports policy versioning and multi-device assignment
- Idempotency via content hash prevents duplicate policy storage

### ✅ Completed (Commit 7a68d70 – Command Generation)

**Policy Command Generation**
- [x] PolicyCommandGenerator service
  - Query active `PolicyAssignment` records
  - Deterministic command ID: MD5(enrollmentId + policyId) → UUID
  - Idempotent: same enrollment+policy always produces same command ID
  - Skips unacknowledged or version-mismatched assignments
  - Skips inactive policies and unenrolled devices
- [x] CommandEventOwnership integration
  - Registers generated commands in database with RESTRICT delete behavior
  - Reflection-based access to internal Create method
  - Handles CommandEventRejectedException gracefully
- [x] Integration test suite (5 tests, all passing)
  - Skip unacknowledged assignments
  - Skip inactive policies
  - Skip unenrolled devices
  - Verify idempotency (same enrollment+policy = same command ID)
  - Filter by enrollment ID

**Verification**
- All tests passing: 31/31 (26 existing + 5 new)
- Command generation deterministic and repeatable
- Database registration successful without duplicates

### ✅ Completed (Commit 5e28811 – Publication Endpoint)

**Policy Command Publication**
- [x] Admin endpoint: `POST /api/v1/policies/publish-commands`
  - Triggers active policy command generation
  - Returns count of commands generated
  - Idempotent: re-calling produces no duplicates
- [x] Error handling
  - 200 OK with CommandsPublished count on success
  - 500 Internal Server Error on generation failure
- [x] Service registration
  - PolicyCommandGenerator injected into PolicyController
  - Dependency injection via ConfigureEnrollment

**Verification**
- All tests passing: 31/31
- Endpoint accessible for administrator-triggered publication
- Ready for integration with scheduled jobs or event-driven triggers

### ✅ Completed (Commit dd6f52e – Android Device Handler)

**Android Device-Side Policy Handler**
- [x] PolicyDatabase (Room)
  - PolicyRow entity with full version tracking
  - Fields: policyId, assignedVersion, acknowledgedVersion, policyJson, isActive, timestamps
  - PolicyDao CRUD and query methods
- [x] PolicyHandler
  - Process SET_POLICY commands from backend
  - Idempotent: same command ID never creates duplicates
  - Version tracking: assigned (from backend) vs. acknowledged (from device)
  - Support policy queries and acknowledgement updates
- [x] CommandReceiver
  - MQTT subscription to device command topic
  - Dispatch SET_POLICY commands to PolicyHandler
  - MqttCommandService for lifecycle management
- [x] EventDeliverySession integration
  - PolicyDatabase and PolicyHandler available in session
  - Seamless integration with CommandJournal
- [x] Unit test suite (7 tests, all passing)
  - Policy creation from SET_POLICY command
  - Version update handling
  - Idempotent re-reception
  - Acknowledgement version tracking
  - Current policy retrieval
- [x] Documentation: android-policy-handler.md
  - Architecture overview
  - Component descriptions
  - Command flow and version tracking
  - Error handling and security notes

**Verification**
- All Android tests pass (unit test suite created)
- Database schema supports version tracking
- CommandReceiver integrates with EventUploadWorker
- MQTT subscription pattern follows fleet/devices/{deviceId}/commands convention

### ✅ Completed (Commit 386bab5 – PostgreSQL Integration Tests)

**Backend Database Integration Tests**
- [x] PolicyPersistenceTests: Real PostgreSQL validation
  - PolicyCreationIsIdempotent: Same JSON produces same policy ID
  - PolicyAssignmentTracksVersions: Assignment with real enrolled device
  - PolicyCommandGenerationWithRealDatabase: End-to-end workflow
- [x] Enrollment integration
  - Uses EnrollmentBootstrapTokens and PostgresEnrollmentClaims patterns
  - Creates real enrolled devices for assignment testing
- [x] Schema isolation
  - Each test creates isolated PostgreSQL schema
  - Automatic cleanup on test completion
  - Requires FLEET_TEST_POSTGRES environment variable
- [x] Idempotency verification
  - Re-running command generation produces no duplicates
  - Same policy + enrollment → same command ID

**Verification**
- All tests passing: 31 unit + 3 integration = 34 total
- Real PostgreSQL validation for policy persistence
- Full workflow tested: create → assign → generate → idempotency check
- Ready for MQTT end-to-end testing

### Summary of Phase 2.1

**Backend (Complete):**
- Policy CRUD with idempotent creation ✅
- Policy assignment and version tracking ✅
- Command generation with deterministic IDs ✅
- Publication endpoint for admin-triggered delivery ✅
- PostgreSQL integration tests (3 tests, all passing) ✅

**Android (Complete):**
- Policy persistence with Room database ✅
- SET_POLICY command processing ✅
- Version tracking (assigned and acknowledged) ✅
- MQTT command reception ✅
- Unit tests (7 tests, all passing) ✅

**Testing (Complete):**
- 31 backend unit tests (all passing)
- 3 backend PostgreSQL integration tests (all passing)
- 7 Android unit tests (all passing)
- Total: 41 tests (all passing)

### 🔄 Next Steps (Phase 2.1 Final Tasks)

1. **MQTT Integration Tests** (1–2 hours)
   - Use aMQTT broker (from Phase 1)
   - Device receives SET_POLICY commands via MQTT
   - Verify CommandReceiver subscription and dispatch
   - Test version tracking end-to-end
   - Device sends receipt event via CommandJournal

2. **E2E Verification Harness** (1–2 hours)
   - Combined Android/backend test harness
   - Admin creates policy (verify idempotency: same JSON twice → same ID)
   - Admin assigns policy to test device
   - Backend generates SET_POLICY command via /api/v1/policies/publish-commands
   - MQTT broker delivers command to device
   - Device receives via CommandReceiver, dispatches to PolicyHandler
   - Device persists to PolicyDatabase with version tracking
   - Device sends receipt event via EventUploadWorker
   - Admin observes assignment progression

3. **Documentation Updates** (30 minutes)
   - Update PHASE2-PLAN with completion status
   - Create integration test guide with example commands
   - Document MQTT subscription topics and payloads

4. **Policy Application Handler** (Future — Phase 2.2)
   - Device application layer listens for new policies
   - Parses policyJson and applies settings
   - On success, calls PolicyHandler.acknowledgePolicyVersion()
   - Enables version-aware deployment tracking
   - Completes version acknowledgement workflow

### Status
- **Milestone**: Policy Delivery Foundation (Backend + Android)
- **Branch**: main
- **Commits**:
  - b51cdaf: Policy models, service, REST endpoints, tests (26 tests)
  - 7a68d70: Command generation service with idempotency (5 tests)
  - 5e28811: Publication endpoint for admin trigger
  - dd6f52e: Android device handler, MQTT reception, unit tests (7 tests)
  - 386bab5: PostgreSQL integration tests (3 tests)
- **Test Summary**: 31 backend unit + 3 integration + 7 Android = 41 total tests (all passing)
- **Estimated Phase 2.1 completion**: After MQTT integration tests and E2E verification
- **Estimated Phase 2.2 start**: Policy application layer, release deployment pipeline, management console

