package dev.fleet.agent

import com.squareup.moshi.JsonReader
import com.squareup.moshi.Moshi
import okio.Buffer
import org.junit.Assert.*
import org.junit.Test
import java.time.Instant
import java.util.UUID

class CommandJournalWireTest {
    private val deviceId = UUID.fromString("a75df51f-ce19-40b7-8370-2c1a745bc823")

    @Test fun canonicalCommandsRoundTripSharedFixturesWithoutFloatingPointLoss() {
        val text = requireNotNull(javaClass.getResourceAsStream("/commands-v1.json"))
            .bufferedReader().use { it.readText() }
        val suite = JsonReader.of(Buffer().writeUtf8(text)).use { it.readJsonValue() as Map<*, *> }
        val adapter = Moshi.Builder().build().adapter(Any::class.java)
        (suite["valid"] as List<*>).forEach { fixture ->
            val wire = adapter.toJson(fixture).toByteArray()
            val command = CommandDecoder.decode(wire, deviceId)
            val canonical = CommandJournalWire.command(command)
            assertEquals(command, CommandDecoder.decode(canonical.toByteArray(), deviceId))
            assertEquals(canonical, CommandJournalWire.command(CommandDecoder.decode(canonical.toByteArray(), deviceId)))
            val maximum = canonical.replace("\"sequence\":${command.sequence}", "\"sequence\":9007199254740991")
            assertTrue(CommandJournalWire.command(CommandDecoder.decode(maximum.toByteArray(), deviceId))
                .contains("\"sequence\":9007199254740991,"))
        }
    }

    @Test fun receiptEventsMatchTheirContractFieldsAndStableCallerSuppliedIds() {
        val command = CommandMessage(UUID.randomUUID(), deviceId, 1, CommandPayload.RequestSync,
            Instant.parse("2026-10-17T09:00:00Z"), Instant.parse("2026-10-17T11:00:00Z"), UUID.randomUUID())
        val eventId = UUID.randomUUID()
        val now = Instant.parse("2026-10-17T10:00:00Z")
        ReceiptState.entries.forEach { state ->
            val wire = CommandJournalWire.event(command, eventId, state, now)
            val fields = JsonReader.of(Buffer().writeUtf8(wire)).use { it.readJsonValue() as Map<*, *> }
            assertEquals(mapOf("schemaVersion" to 1.0, "eventId" to eventId.toString(),
                "commandId" to command.commandId.toString(), "deviceId" to deviceId.toString(),
                "correlationId" to command.correlationId.toString(), "recordedAt" to now.toString(),
                "state" to state.wireName), fields)
            assertEquals(wire, CommandJournalWire.event(command, eventId, state, now))
        }
        val fixtures = requireNotNull(javaClass.getResourceAsStream("/command-events-v1.json"))
            .bufferedReader().use { it.readText() }
        val suite = JsonReader.of(Buffer().writeUtf8(fixtures)).use { it.readJsonValue() as Map<*, *> }
        val receiptFixtures = (suite["valid"] as List<*>).map { it as Map<*, *> }
            .filter { it["state"] in setOf("received", "expired") }
        assertEquals(2, receiptFixtures.size)
        receiptFixtures.forEach { fixture ->
            val state = ReceiptState.entries.single { it.wireName == fixture["state"] }
            val recordedAt = Instant.parse(fixture["recordedAt"] as String)
            val fixtureCommand = command.copy(commandId = UUID.fromString(fixture["commandId"] as String),
                deviceId = UUID.fromString(fixture["deviceId"] as String),
                correlationId = UUID.fromString(fixture["correlationId"] as String))
            val wire = CommandJournalWire.event(fixtureCommand, UUID.fromString(fixture["eventId"] as String), state, recordedAt)
            assertEquals(fixture, JsonReader.of(Buffer().writeUtf8(wire)).use { it.readJsonValue() })
        }
    }
}