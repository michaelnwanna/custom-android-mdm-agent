package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import org.junit.Test
import java.time.Instant
import java.util.UUID
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class PolicyHandlerTest {
    private val context = org.robolectric.RuntimeEnvironment.getApplication()
    private val policyDb = PolicyDatabase.open(context)
    private val deviceId = UUID.fromString("f47ac10b-58cc-4372-a567-0e02b2c3d479")
    private val handler = PolicyHandler(policyDb, deviceId)

    @Test
    fun handleSetPolicyCommand_WithNewPolicy_CreatesPolicy() {
        val policyId = UUID.randomUUID()
        val policyVersion = 1L
        val command = CommandMessage(
            commandId = UUID.randomUUID(),
            deviceId = deviceId,
            sequence = 1,
            payload = CommandPayload.SetPolicy(policyId, policyVersion),
            issuedAt = Instant.now(),
            expiresAt = Instant.now().plusSeconds(3600),
            correlationId = UUID.randomUUID()
        )

        val result = handler.handleSetPolicyCommand(command)

        assertIs<PolicyHandlerResult.Created>(result)
        assertEquals(policyId.toString(), result.policyId)
        assertEquals(policyVersion, result.assignedVersion)

        val storedPolicy = handler.getCurrentPolicy()
        assertNotNull(storedPolicy)
        assertEquals(policyId.toString(), storedPolicy.policyId)
        assertEquals(policyVersion, storedPolicy.assignedVersion)
        assertNull(storedPolicy.acknowledgedVersion)
    }

    @Test
    fun handleSetPolicyCommand_WithExistingPolicy_SkipsIfSameVersion() {
        val policyId = UUID.randomUUID()
        val policyVersion = 1L
        val command = CommandMessage(
            commandId = UUID.randomUUID(),
            deviceId = deviceId,
            sequence = 1,
            payload = CommandPayload.SetPolicy(policyId, policyVersion),
            issuedAt = Instant.now(),
            expiresAt = Instant.now().plusSeconds(3600),
            correlationId = UUID.randomUUID()
        )

        // First reception
        val result1 = handler.handleSetPolicyCommand(command)
        assertIs<PolicyHandlerResult.Created>(result1)

        // Second reception of same command (idempotent)
        val result2 = handler.handleSetPolicyCommand(command)
        assertIs<PolicyHandlerResult.AlreadyProcessed>(result2)
        assertEquals(policyId.toString(), result2.policyId)
    }

    @Test
    fun handleSetPolicyCommand_WithNewerVersion_Updates() {
        val policyId = UUID.randomUUID()
        
        // First version
        val command1 = CommandMessage(
            commandId = UUID.randomUUID(),
            deviceId = deviceId,
            sequence = 1,
            payload = CommandPayload.SetPolicy(policyId, 1),
            issuedAt = Instant.now(),
            expiresAt = Instant.now().plusSeconds(3600),
            correlationId = UUID.randomUUID()
        )
        val result1 = handler.handleSetPolicyCommand(command1)
        assertIs<PolicyHandlerResult.Created>(result1)

        // Newer version of same policy
        val command2 = CommandMessage(
            commandId = UUID.randomUUID(),
            deviceId = deviceId,
            sequence = 2,
            payload = CommandPayload.SetPolicy(policyId, 2),
            issuedAt = Instant.now(),
            expiresAt = Instant.now().plusSeconds(3600),
            correlationId = UUID.randomUUID()
        )
        val result2 = handler.handleSetPolicyCommand(command2)
        assertIs<PolicyHandlerResult.Updated>(result2)
        assertEquals(2L, result2.assignedVersion)

        val storedPolicy = handler.getCurrentPolicy()
        assertNotNull(storedPolicy)
        assertEquals(2L, storedPolicy.assignedVersion)
    }

    @Test
    fun acknowledgePolicyVersion_UpdatesAcknowledgedVersion() {
        val policyId = UUID.randomUUID()
        val policyVersion = 2L

        // Create policy
        val command = CommandMessage(
            commandId = UUID.randomUUID(),
            deviceId = deviceId,
            sequence = 1,
            payload = CommandPayload.SetPolicy(policyId, policyVersion),
            issuedAt = Instant.now(),
            expiresAt = Instant.now().plusSeconds(3600),
            correlationId = UUID.randomUUID()
        )
        handler.handleSetPolicyCommand(command)

        // Acknowledge
        handler.acknowledgePolicyVersion(policyId.toString(), policyVersion)

        val storedPolicy = handler.getCurrentPolicy()
        assertNotNull(storedPolicy)
        assertEquals(policyVersion, storedPolicy.acknowledgedVersion)
        assertNotNull(storedPolicy.acknowledgedAt)
    }

    @Test
    fun getCurrentPolicy_ReturnsNullWhenNoPolicies() {
        val policy = handler.getCurrentPolicy()
        assertNull(policy)
    }

    @Test
    fun getCurrentPolicy_ReturnsLatestActive() {
        val policyId1 = UUID.randomUUID()
        val policyId2 = UUID.randomUUID()

        val command1 = CommandMessage(
            commandId = UUID.randomUUID(),
            deviceId = deviceId,
            sequence = 1,
            payload = CommandPayload.SetPolicy(policyId1, 1),
            issuedAt = Instant.now(),
            expiresAt = Instant.now().plusSeconds(3600),
            correlationId = UUID.randomUUID()
        )
        handler.handleSetPolicyCommand(command1)

        Thread.sleep(10)

        val command2 = CommandMessage(
            commandId = UUID.randomUUID(),
            deviceId = deviceId,
            sequence = 2,
            payload = CommandPayload.SetPolicy(policyId2, 1),
            issuedAt = Instant.now(),
            expiresAt = Instant.now().plusSeconds(3600),
            correlationId = UUID.randomUUID()
        )
        handler.handleSetPolicyCommand(command2)

        val current = handler.getCurrentPolicy()
        assertNotNull(current)
        assertEquals(policyId2.toString(), current.policyId)
    }
}
