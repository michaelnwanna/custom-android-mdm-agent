package dev.fleet.agent

import com.squareup.moshi.Moshi
import org.junit.Assert.*
import org.junit.Test
import java.util.UUID

class EventAcknowledgementTest {
    private val deviceId = UUID.fromString("90751d25-a92a-4f30-bd2e-55e3e87245d0")
    private val eventId = UUID.fromString("39459be6-b32c-4e5b-9b4b-cb76a613a4a2")
    private fun body(time: String = "2026-10-17T12:00:00.123456789Z") =
        """{"schemaVersion":1,"deviceId":"$deviceId","eventId":"$eventId","receivedAt":"$time"}"""

    @Test fun sharedAcknowledgementFixturesAndMutationsMatchTheReceiver() {
        val adapter = Moshi.Builder().build().adapter(Map::class.java)
        val suite = adapter.fromJson(javaClass.getResourceAsStream("/event-acks-v1.json")!!.bufferedReader().use { it.readText() })!!
        val valid = suite["valid"] as List<*>
        for (entry in valid) {
            val message = entry as Map<*, *>
            val expectedDevice = UUID.fromString(message["deviceId"] as String)
            val expectedEvent = UUID.fromString(message["eventId"] as String)
            val decoded = EventAcknowledgementDecoder.decode(adapter.toJson(message).toByteArray(), expectedDevice, expectedEvent)
            assertEquals(message["receivedAt"], decoded.receivedAt)
        }
        for (entry in suite["invalid"] as List<*>) {
            val mutation = entry as Map<*, *>
            val message = (valid[(mutation["base"] as Double).toInt()] as Map<*, *>).toMutableMap()
            val expectedDevice = UUID.fromString(message["deviceId"] as String)
            val expectedEvent = UUID.fromString(message["eventId"] as String)
            (mutation["set"] as? Map<*, *>)?.let { message.putAll(it) }
            (mutation["remove"] as? List<*>)?.forEach { message.remove(it) }
            assertThrows(Exception::class.java) {
                EventAcknowledgementDecoder.decode(adapter.toJson(message).toByteArray(), expectedDevice, expectedEvent)
            }
        }
    }

    @Test fun exactBindingFieldsTypesUtf8AndTimestampPrecisionAreRequired() {
        val original = body()
        val accepted = EventAcknowledgementDecoder.decode(original.toByteArray(), deviceId, eventId)
        assertEquals("2026-10-17T12:00:00.123456789Z", accepted.receivedAt)
        for (version in listOf("1.0", "1e0", "10e-1"))
            assertEquals(accepted, EventAcknowledgementDecoder.decode(original.replace(":1,", ":$version,").toByteArray(), deviceId, eventId))
        assertEquals(accepted, EventAcknowledgementDecoder.decode(original.replace(deviceId.toString(), deviceId.toString().uppercase())
            .toByteArray(), deviceId, eventId))
        val rejected = listOf("", "{}", "$original {}", original.replace(":1,", ":true,"),
            original.replace(":1,", ":\"1\","), original.replace(":1,", ":1.00000000000000001,"),
            original.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            original.replace("\"receivedAt\":", "\"extra\":0,\"receivedAt\":"),
            original.replace(deviceId.toString(), UUID.randomUUID().toString()),
            original.replace(eventId.toString(), UUID.randomUUID().toString()),
            original.replace(deviceId.toString(), " $deviceId"), original.replace("\"receivedAt\":\"2026-10-17T12:00:00.123456789Z\"", "\"receivedAt\":null"),
            body("2026-02-30T12:00:00Z"), body("0000-01-01T00:00:00Z"), body("2026-10-17T12:00:60Z"),
            body("2026-10-17T12:00:00.1234567890Z"), body("2026-10-17T12:00:00"), " ".repeat(4097))
        for (candidate in rejected) assertThrows(Exception::class.java) {
            EventAcknowledgementDecoder.decode(candidate.toByteArray(), deviceId, eventId)
        }
        assertThrows(Exception::class.java) {
            EventAcknowledgementDecoder.decode(byteArrayOf(0xc3.toByte(), 0x28), deviceId, eventId)
        }
    }
}