package dev.fleet.agent

import com.squareup.moshi.JsonReader
import com.squareup.moshi.Moshi
import okio.Buffer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.time.Instant
import java.util.UUID

class CommandDecoderTest {
    private val deviceId = UUID.fromString("a75df51f-ce19-40b7-8370-2c1a745bc823")
    private val command = """{"schemaVersion":1,"commandId":"7512f600-bb5b-4353-b67a-3a56126308d7","deviceId":"$deviceId","sequence":1,"type":"REQUEST_SYNC","payload":{},"issuedAt":"2026-10-16T12:00:00Z","expiresAt":"2026-10-16T13:00:00Z","correlationId":"374823b7-0e54-4893-b661-e638cc03310c"}"""

    @Test fun sharedContractFixturesAgreeWithTheDeviceDecoder() {
        val text = requireNotNull(javaClass.getResourceAsStream("/commands-v1.json"))
            .bufferedReader().use { it.readText() }
        val suite = JsonReader.of(Buffer().writeUtf8(text)).use { it.readJsonValue() as Map<*, *> }
        val valid = suite["valid"] as List<*>
        val adapter = Moshi.Builder().build().adapter(Any::class.java)
        assertEquals(3, valid.size)
        val commands = valid.map { CommandDecoder.decode(adapter.toJson(it).toByteArray(), deviceId) }
        assertEquals(CommandPayload.SetPolicy(UUID.fromString("bb8a0a1f-b173-4dc9-9c92-d27a70d28626"), 1),
            commands[0].payload)
        assertEquals(CommandPayload.InstallRelease(UUID.fromString("7e3f4031-a755-44f0-a380-c41c716d25e5")),
            commands[1].payload)
        assertEquals(CommandPayload.RequestSync, commands[2].payload)
        val invalid = suite["invalid"] as List<*>
        assertEquals(14, invalid.size)
        invalid.forEach { entry ->
            val mutation = entry as Map<*, *>
            val base = valid[(mutation["base"] as Number).toInt()] as Map<*, *>
            val changed = base.entries.associate { it.key as String to it.value }.toMutableMap()
            (mutation["set"] as? Map<*, *>)?.forEach { (key, value) -> changed[key as String] = value }
            (mutation["remove"] as? List<*>)?.forEach { changed.remove(it as String) }
            assertThrows(mutation["name"] as String, Exception::class.java) {
                CommandDecoder.decode(adapter.toJson(changed).toByteArray(), deviceId)
            }
        }
    }

    @Test fun normalizesIdentifiersOffsetsAndExactJsonIntegers() {
        val adjusted = command.replace("7512f600-bb5b-4353-b67a-3a56126308d7", "7512F600-BB5B-4353-B67A-3A56126308D7")
            .replace("12:00:00Z", "14:00:00+02:00").replace("13:00:00Z", "14:00:00+01:00")
        assertEquals(CommandDecoder.decode(command.toByteArray(), deviceId),
            CommandDecoder.decode(adjusted.toByteArray(), deviceId))
        assertEquals(9007199254740991L, CommandDecoder.decode(command.replace("\"sequence\":1",
            "\"sequence\":9007199254740991.0").toByteArray(), deviceId).sequence)
        val fraction = command.replace("12:00:00Z", "12:00:00.123456789z")
        assertEquals(123456789, CommandDecoder.decode(fraction.toByteArray(), deviceId).issuedAt.nano)
        val padded = command + " ".repeat(CommandDecoder.MAXIMUM_BYTES - command.toByteArray().size)
        assertEquals(CommandDecoder.decode(command.toByteArray(), deviceId),
            CommandDecoder.decode(padded.toByteArray(), deviceId))
    }

    @Test fun rejectsMissingFieldsAndEveryPayloadMismatch() {
        val reader = JsonReader.of(Buffer().writeUtf8(command))
        val fields = reader.use { it.readJsonValue() as Map<*, *> }
        val adapter = Moshi.Builder().build().adapter(Any::class.java)
        fields.keys.forEach { missing ->
            val reduced = fields.filterKeys { it != missing }
            assertThrows("Missing $missing", Exception::class.java) {
                CommandDecoder.decode(adapter.toJson(reduced).toByteArray(), deviceId)
            }
        }
        val policy = """{"policyId":"bb8a0a1f-b173-4dc9-9c92-d27a70d28626","policyVersion":1}"""
        val invalid = listOf(
            command.replace("REQUEST_SYNC", "SET_POLICY"),
            command.replace("REQUEST_SYNC", "INSTALL_RELEASE"),
            command.replace("{}", policy),
            command.replace("REQUEST_SYNC", "INSTALL_RELEASE").replace("{}", policy),
            command.replace("REQUEST_SYNC", "SET_POLICY").replace("{}", policy.replace("\"policyVersion\":1",
                "\"policyVersion\":1,\"policyVersion\":2")),
            command.replace("REQUEST_SYNC", "SET_POLICY").replace("{}", policy.replace("\"policyVersion\":1",
                "\"policyVersion\":\"1\"")),
        )
        invalid.forEach { text -> assertThrows(Exception::class.java) {
            CommandDecoder.decode(text.toByteArray(), deviceId)
        } }
    }

    @Test fun decodesTypedCommandWithoutTreatingItAsExecutionAcceptance() {
        val parsed = CommandDecoder.decode(command.toByteArray(), deviceId)
        assertEquals(CommandPayload.RequestSync, parsed.payload)
        assertEquals(1L, parsed.sequence)
        assertEquals(Instant.parse("2026-10-16T12:00:00Z"), parsed.issuedAt)
        assertEquals(deviceId, parsed.deviceId)
        assertEquals(parsed, CommandDecoder.decode(command.replace("\"sequence\":1", "\"sequence\":1e0")
            .toByteArray(), deviceId))
    }

    @Test fun rejectsMalformedWireValuesAndWrongDevice() {
        val invalid = listOf(
            command.replace("\"sequence\":1", "\"sequence\":1,\"sequence\":2"),
            command.replace("\"payload\":{}", "\"payload\":{},\"payload\":{}"),
            command.replace("\"sequence\":1", "\"sequence\":\"1\""),
            command.replace("\"sequence\":1", "\"sequence\":1.5"),
            command.replace("\"sequence\":1", "\"sequence\":9007199254740992"),
            command.replace("\"schemaVersion\":1", "\"schemaVersion\":true"),
            command.replace("\"payload\":{}", "\"payload\":null"),
            command.replace("13:00:00Z", "12:00:00Z"),
            command.replace("13:00:00Z", "11:00:00Z"),
            command.replace("12:00:00Z", "24:00:00Z"),
            command.replace("12:00:00Z", "12:00:00"),
            command.replace("12:00:00Z", "12:00:00.1234567891Z"),
            command.replace("2026-10-16T12", "2026-02-30T12"),
            command.replace("7512f600-bb5b", "1-bb5b"),
            command.replace("\"payload\":{}", "\"payload\":{\"force\":true}"),
            command.replace("\"type\":\"REQUEST_SYNC\"", "\"type\":\"EXECUTE_SHELL\""),
            command + "{}", command + "//comment", command.replace("\"schemaVersion\"", "schemaVersion"),
        )
        invalid.forEach { candidate ->
            assertThrows(candidate, Exception::class.java) { CommandDecoder.decode(candidate.toByteArray(), deviceId) }
        }
        assertThrows(Exception::class.java) { CommandDecoder.decode(command.toByteArray(), UUID.randomUUID()) }
        assertThrows(Exception::class.java) { CommandDecoder.decode(byteArrayOf(0xc3.toByte(), 0x28), deviceId) }
        assertThrows(Exception::class.java) { CommandDecoder.decode(ByteArray(CommandDecoder.MAXIMUM_BYTES + 1), deviceId) }
        assertThrows(Exception::class.java) { CommandDecoder.decode(byteArrayOf(), deviceId) }
    }
}