package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.util.UUID

/**
 * Handles SET_POLICY commands from the backend.
 * Persists policy to local database and tracks assigned/acknowledged versions.
 */
internal class PolicyHandler(
    private val database: PolicyDatabase,
    private val deviceId: UUID,
) {
    /**
     * Process a SET_POLICY command by persisting it to the policy database.
     * The policy is tracked with assigned version; acknowledged version is initially null.
     */
    fun handleSetPolicyCommand(command: CommandMessage): PolicyHandlerResult {
        require(command.deviceId == deviceId)
        val payload = command.payload as? CommandPayload.SetPolicy
            ?: throw IllegalArgumentException("Not a SetPolicy command")

        val policyId = payload.policyId.toString()
        val assignedVersion = payload.policyVersion

        return database.runInTransaction<PolicyHandlerResult> {
            val dao = database.policies()
            val existing = dao.policy(policyId)

            if (existing != null) {
                // Policy already received; update assigned version if higher
                if (assignedVersion > existing.assignedVersion) {
                    val updated = existing.copy(
                        assignedVersion = assignedVersion,
                        assignedAt = Instant.now().toString()
                    )
                    dao.updatePolicy(updated)
                    PolicyHandlerResult.Updated(policyId, assignedVersion)
                } else {
                    // Same or older version; no-op
                    PolicyHandlerResult.AlreadyProcessed(policyId, assignedVersion)
                }
            } else {
                // New policy; insert with assigned version and no acknowledged version
                val policy = PolicyRow(
                    policyId = policyId,
                    assignedVersion = assignedVersion,
                    acknowledgedVersion = null,
                    policyJson = "", // Policy JSON not included in command; only ID and version
                    isActive = true,
                    assignedAt = Instant.now().toString(),
                    acknowledgedAt = null
                )
                dao.insertPolicy(policy)
                PolicyHandlerResult.Created(policyId, assignedVersion)
            }
        }
    }

    /**
     * Retrieve the current active policy, if any.
     */
    fun getCurrentPolicy(): PolicyRow? {
        return database.policies().currentPolicy()
    }

    /**
     * Acknowledge a policy by updating the acknowledged version.
     * Called after the policy has been successfully applied on the device.
     */
    fun acknowledgePolicyVersion(policyId: String, acknowledgedVersion: Long) {
        database.runInTransaction {
            val dao = database.policies()
            val policy = dao.policy(policyId) ?: throw IllegalArgumentException("Policy not found")
            val updated = policy.copy(
                acknowledgedVersion = acknowledgedVersion,
                acknowledgedAt = Instant.now().toString()
            )
            dao.updatePolicy(updated)
        }
    }
}

sealed interface PolicyHandlerResult {
    data class Created(val policyId: String, val assignedVersion: Long) : PolicyHandlerResult
    data class Updated(val policyId: String, val assignedVersion: Long) : PolicyHandlerResult
    data class AlreadyProcessed(val policyId: String, val assignedVersion: Long) : PolicyHandlerResult
}
