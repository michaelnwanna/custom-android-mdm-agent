package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import java.math.BigDecimal
import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets
import java.time.OffsetDateTime
import java.util.UUID

internal data class EventAcknowledgement(val deviceId: UUID, val eventId: UUID, val receivedAt: String)

internal object EventAcknowledgementDecoder {
    const val MAXIMUM_BYTES = 4096
    private val fields = setOf("schemaVersion", "deviceId", "eventId", "receivedAt")
    private val timestamp = Regex("[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt][0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{1,9})?([Zz]|[+-][0-9]{2}:[0-9]{2})")

    fun decode(bytes: ByteArray, expectedDeviceId: UUID, expectedEventId: UUID): EventAcknowledgement {
        require(bytes.isNotEmpty() && bytes.size <= MAXIMUM_BYTES) { "Invalid acknowledgement size" }
        val text = StandardCharsets.UTF_8.newDecoder().decode(ByteBuffer.wrap(bytes)).toString()
        JsonReader.of(Buffer().writeUtf8(text)).use { reader ->
            val values = mutableMapOf<String, String>()
            reader.beginObject()
            while (reader.hasNext()) {
                val name = reader.nextName()
                require(name in fields && name !in values) { "Invalid acknowledgement field" }
                if (name == "schemaVersion") {
                    require(reader.peek() == JsonReader.Token.NUMBER)
                    require(BigDecimal(reader.nextString()).compareTo(BigDecimal.ONE) == 0)
                    values[name] = "1"
                } else {
                    require(reader.peek() == JsonReader.Token.STRING)
                    values[name] = reader.nextString()
                }
            }
            reader.endObject()
            require(values.keys == fields && reader.peek() == JsonReader.Token.END_DOCUMENT)
            val deviceId = identifier(values.getValue("deviceId"))
            val eventId = identifier(values.getValue("eventId"))
            require(deviceId == expectedDeviceId && eventId == expectedEventId) { "Acknowledgement identity mismatch" }
            val receivedAt = values.getValue("receivedAt")
            require(timestamp.matches(receivedAt) && receivedAt.substring(0, 4) != "0000")
            OffsetDateTime.parse(receivedAt)
            return EventAcknowledgement(deviceId, eventId, receivedAt)
        }
    }

    private fun identifier(text: String): UUID {
        val identifier = UUID.fromString(text)
        require(identifier.toString().equals(text, ignoreCase = true))
        return identifier
    }
}