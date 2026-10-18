# Android Policy Handler Implementation

## Overview

The Android policy handler is responsible for receiving, processing, and persisting device policies from the backend. Policies are delivered via MQTT as `SET_POLICY` commands and are tracked with version information to support incremental updates.

## Architecture

### Components

#### 1. **PolicyDatabase** (`PolicyDatabase.kt`)
- Room database for persisting policies on device
- Schema:
  - **policies** table: Stores policy state (ID, assigned version, acknowledged version, JSON, active flag, timestamps)
  - DAO methods for CRUD operations and queries

#### 2. **PolicyRow** Entity
- Represents a policy record in the database
- Fields:
  - `policyId` (String, PK): Unique policy identifier (UUID)
  - `assignedVersion` (Long): Version assigned by backend
  - `acknowledgedVersion` (Long?): Highest version applied on device (null initially)
  - `policyJson` (String): Policy configuration (empty in current phase; to be populated when full policy JSON is sent)
  - `isActive` (Boolean): Whether policy is currently active
  - `assignedAt` (String): ISO-8601 timestamp when assigned
  - `acknowledgedAt` (String?): ISO-8601 timestamp when acknowledged

#### 3. **PolicyHandler** (`PolicyHandler.kt`)
- Main handler for processing `SET_POLICY` commands
- Key responsibilities:
  - Decode command payloads
  - Persist new policies or update existing ones
  - Track assigned and acknowledged versions
  - Support idempotent command processing

**Methods:**
```kotlin
fun handleSetPolicyCommand(command: CommandMessage): PolicyHandlerResult
fun getCurrentPolicy(): PolicyRow?
fun acknowledgePolicyVersion(policyId: String, acknowledgedVersion: Long): Unit
```

#### 4. **CommandReceiver** (`CommandReceiver.kt`)
- Listens for incoming `SET_POLICY` commands via MQTT
- Dispatches commands to appropriate handlers (PolicyHandler, release installer, etc.)
- Maintains subscription to device command topic: `fleet/devices/{deviceId}/commands`

#### 5. **MqttCommandService** (`CommandReceiver.kt`)
- Manages MQTT client lifecycle
- Coordinates command reception and persistence
- Integrates with existing CommandJournal for event tracking

### Integration Points

#### With EventUploadWorker
- `EventDeliverySession` now includes both `PolicyDatabase` and `PolicyHandler`
- Policies are persisted during command reception
- Event acknowledgements are sent separately via `CommandEventTransport`

#### With CommandJournal
- `CommandJournal.receive()` continues to work as before
- Policy-specific processing happens after command is persisted in command journal
- Allows decoupling of command receipt from policy application

## Command Flow

```
1. Backend: Creates policy, derives idempotent ID from JSON
2. Backend: Assigns policy to device, increments assigned version
3. Backend: Publishes SET_POLICY command to MQTT
4. Device (CommandReceiver): Subscribes to fleet/devices/{deviceId}/commands
5. Device (MQTT): Receives command on subscription
6. Device (CommandReceiver): Dispatches to PolicyHandler
7. Device (PolicyHandler): 
   - Checks for existing policy by ID
   - If new: Creates PolicyRow with assigned version
   - If exists with higher version: Updates assigned version
   - If exists with same/lower version: No-op (idempotent)
8. Device (CommandJournal): Logs command event to outbox
9. Device (EventUploadWorker): Sends command receipt event to backend
10. Backend: Tracks assignment acknowledged when device confirms receipt
```

## Version Tracking

### Assigned Version
- Set by backend when creating assignment
- Incremented when policy content changes
- Device receives via SET_POLICY command
- Stored in `assignedVersion` column

### Acknowledged Version
- Initially null (not yet applied)
- Device updates after successfully applying policy
- Stored in `acknowledgedVersion` column
- Enables backend to detect out-of-sync devices

### Device-to-Backend Synchronization
- Device sends acknowledgement event with:
  - Command ID (the SET_POLICY command)
  - Event ID (receipt event)
  - State (RECEIVED or EXPIRED)
  - Backend then updates `PolicyAssignment.AcknowledgedVersion`

## Idempotent Processing

### Command-Level Idempotency
- `CommandJournal` ensures command ID uniqueness
- Duplicate commands (same ID) are detected and skipped
- Transaction ensures no duplicate rows are inserted

### Policy-Level Idempotency
- `PolicyHandler.handleSetPolicyCommand()` checks existing policy by ID
- Same policy ID with same version: Returns `AlreadyProcessed`
- Same policy ID with higher version: Updates with new version
- Newer version never overwrites older version (versioning constraint)

## Error Handling

### Command Reception Errors
- Invalid command format: Caught by `CommandDecoder`, logged
- Device ID mismatch: Rejected by `CommandDecoder`
- Corrupt MQTT payload: Caught and logged, doesn't crash receiver

### Database Errors
- Transaction rollback on constraint violations
- Policy ID conflicts: Handled by existing/update check
- Version constraints: Enforced by comparing versions before update

## Future Enhancements

### Phase 2.2: Policy Content Delivery
- Backend will send full policy JSON in SET_POLICY command (currently ID + version only)
- Device persists JSON to `policyJson` field
- Device-side policy application logic will parse and apply JSON settings

### Phase 2.3: Policy Application Tracking
- Device application layer listens for new policies
- Applies policy settings (network config, restrictions, etc.)
- On successful application, calls `PolicyHandler.acknowledgePolicyVersion()`
- Enables version-aware deployment tracking on backend

### Scheduled Policy Generation
- Backend can implement scheduled job to regenerate SET_POLICY commands
- Idempotent: Re-publishing same command (same ID) produces no duplicates
- Allows recovery if device missed initial delivery

## Testing

### Unit Tests: `PolicyHandlerTest.kt`
- Policy creation from SET_POLICY command
- Version update handling
- Idempotent re-reception
- Acknowledgement tracking
- Current policy retrieval

### Integration Tests (Future)
- E2E with real MQTT broker (aMQTT)
- Command reception → persistence → event upload
- Version mismatch detection
- Device acknowledgement tracking on backend

## Security Considerations

1. **Command Origin**: Verified by TLS mutual authentication
2. **Command Expiration**: Enforced by `CommandDecoder` (expiresAt > now)
3. **Device ID Matching**: `CommandDecoder` verifies command.deviceId == enrolled device ID
4. **Sequence Numbers**: `CommandJournal` enforces per-device uniqueness
5. **Policy Persistence**: Stored in app-private storage (protected by Android sandbox)

## References

- Backend: `backend/src/FleetManagement/Policies/PolicyService.cs`
- Backend Command Generator: `backend/src/FleetManagement/Policies/PolicyCommandGenerator.cs`
- Command Message Format: `android/agent/src/main/java/dev/fleet/agent/CommandMessage.kt`
- MQTT Transport: `android/agent/src/main/java/dev/fleet/agent/DeviceMqttTransport.kt`
