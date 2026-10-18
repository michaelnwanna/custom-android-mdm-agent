package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import java.math.BigDecimal
import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.time.OffsetDateTime
import java.util.UUID

internal sealed interface CommandPayload {
    data class SetPolicy(val policyId: UUID, val policyVersion: Long) : CommandPayload
    data class InstallRelease(val releaseId: UUID) : CommandPayload
    data object RequestSync : CommandPayload
}

internal data class CommandMessage(
    val commandId: UUID,
    val deviceId: UUID,
    val sequence: Long,
    val payload: CommandPayload,
    val issuedAt: Instant,
    val expiresAt: Instant,
    val correlationId: UUID,
)

internal object CommandDecoder {
    const val MAXIMUM_BYTES = 8192
    private const val MAXIMUM_INTEGER = 9007199254740991L
    private val timestamp = Regex("[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt][0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{1,9})?([Zz]|[+-][0-9]{2}:[0-9]{2})")
    private val envelopeFields = setOf("schemaVersion", "commandId", "deviceId", "sequence", "type",
        "payload", "issuedAt", "expiresAt", "correlationId")
    private val payloadFields = setOf("policyId", "policyVersion", "releaseId")

    fun decode(bytes: ByteArray, expectedDeviceId: UUID): CommandMessage {
        require(bytes.isNotEmpty() && bytes.size <= MAXIMUM_BYTES) { "Invalid command size" }
        val text = StandardCharsets.UTF_8.newDecoder().decode(ByteBuffer.wrap(bytes)).toString()
        JsonReader.of(Buffer().writeUtf8(text)).use { reader ->
            val fields = objectFields(reader, envelopeFields, true)
            require(reader.peek() == JsonReader.Token.END_DOCUMENT)
            require(fields.keys == envelopeFields && fields["schemaVersion"] == 1L)
            val deviceId = identifier(fields.getValue("deviceId"))
            require(deviceId == expectedDeviceId) { "Command device mismatch" }
            val payloadFields = fields.getValue("payload") as Map<*, *>
            val payload = when (fields["type"]) {
                "SET_POLICY" -> {
                    require(payloadFields.keys == setOf("policyId", "policyVersion"))
                    CommandPayload.SetPolicy(identifier(payloadFields["policyId"]),
                        payloadFields["policyVersion"] as Long)
                }
                "INSTALL_RELEASE" -> {
                    require(payloadFields.keys == setOf("releaseId"))
                    CommandPayload.InstallRelease(identifier(payloadFields["releaseId"]))
                }
                "REQUEST_SYNC" -> {
                    require(payloadFields.isEmpty())
                    CommandPayload.RequestSync
                }
                else -> throw IllegalArgumentException("Unsupported command type")
            }
            val issuedAt = instant(fields.getValue("issuedAt"))
            val expiresAt = instant(fields.getValue("expiresAt"))
            require(expiresAt > issuedAt) { "Invalid command lifetime" }
            return CommandMessage(identifier(fields.getValue("commandId")), deviceId,
                fields.getValue("sequence") as Long, payload, issuedAt, expiresAt,
                identifier(fields.getValue("correlationId")))
        }
    }

    private fun objectFields(reader: JsonReader, allowed: Set<String>, envelope: Boolean): Map<String, Any> {
        reader.beginObject()
        val fields = mutableMapOf<String, Any>()
        while (reader.hasNext()) {
            val name = reader.nextName()
            require(name in allowed && name !in fields) { "Unknown or duplicate command field" }
            fields[name] = when {
                envelope && name == "payload" -> objectFields(reader, payloadFields, false)
                name in setOf("schemaVersion", "sequence", "policyVersion") -> {
                    require(reader.peek() == JsonReader.Token.NUMBER)
                    val value = BigDecimal(reader.nextString()).longValueExact()
                    require(value in 1..MAXIMUM_INTEGER)
                    value
                }
                else -> {
                    require(reader.peek() == JsonReader.Token.STRING)
                    reader.nextString()
                }
            }
        }
        reader.endObject()
        return fields
    }

    private fun identifier(value: Any?): UUID {
        val text = value as String
        val identifier = UUID.fromString(text)
        require(identifier.toString().equals(text, ignoreCase = true))
        return identifier
    }

    private fun instant(value: Any): Instant {
        val text = value as String
        require(timestamp.matches(text))
        return OffsetDateTime.parse(text).toInstant()
    }
}