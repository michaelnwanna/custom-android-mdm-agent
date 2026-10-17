package dev.fleet.agent

import com.squareup.moshi.JsonWriter
import okio.Buffer
import java.time.Clock
import java.time.Instant
import java.util.UUID
import java.util.concurrent.Callable

internal enum class ReceiptState(val wireName: String) {
    RECEIVED("received"), EXPIRED("expired")
}

internal data class CommandReceipt(val commandId: UUID, val eventId: UUID, val state: ReceiptState, val duplicate: Boolean)

internal class CommandJournal(
    private val database: CommandDatabase,
    private val deviceId: UUID,
    private val clock: Clock = Clock.systemUTC(),
) {
    fun receiveFromMqtt(topic: String, bytes: ByteArray, retained: Boolean): CommandReceipt {
        require(topic == "fleet/devices/$deviceId/commands" && !retained) { "Invalid command delivery" }
        return receive(bytes)
    }

    fun receive(bytes: ByteArray): CommandReceipt {
        val command = CommandDecoder.decode(bytes, deviceId)
        val canonical = CommandJournalWire.command(command)
        return database.runInTransaction(Callable {
            val dao = database.journal()
            val existing = dao.command(command.commandId.toString())
            if (existing != null) {
                require(existing.deviceId == deviceId.toString() && existing.canonicalJson == canonical) {
                    "Command identity conflict"
                }
                CommandReceipt(command.commandId, UUID.fromString(existing.receiptEventId),
                    ReceiptState.valueOf(existing.initialState), true)
            } else {
                require(dao.sequence(deviceId.toString(), command.sequence) == null) { "Command sequence conflict" }
                val now = clock.instant()
                val state = if (now < command.expiresAt) ReceiptState.RECEIVED else ReceiptState.EXPIRED
                val eventId = UUID.randomUUID()
                dao.insertCommand(CommandInboxRow(command.commandId.toString(), deviceId.toString(), command.sequence,
                    canonical, eventId.toString(), state.name))
                dao.insertEvent(CommandEventRow(eventId.toString(), command.commandId.toString(), deviceId.toString(),
                    CommandJournalWire.event(command, eventId, state, now)))
                CommandReceipt(command.commandId, eventId, state, false)
            }
        })
    }

    fun pendingEvents(limit: Int = 100): List<CommandEventRow> {
        require(limit in 1..100)
        return database.journal().pendingEvents(deviceId.toString(), limit)
    }
}

internal object CommandJournalWire {
    fun command(command: CommandMessage): String = json { writer ->
        writer.name("schemaVersion").value(1L)
        writer.name("commandId").value(command.commandId.toString())
        writer.name("deviceId").value(command.deviceId.toString())
        writer.name("sequence").value(command.sequence)
        writer.name("type").value(when (command.payload) {
            is CommandPayload.SetPolicy -> "SET_POLICY"
            is CommandPayload.InstallRelease -> "INSTALL_RELEASE"
            CommandPayload.RequestSync -> "REQUEST_SYNC"
        })
        writer.name("payload").beginObject()
        when (val payload = command.payload) {
            is CommandPayload.SetPolicy -> {
                writer.name("policyId").value(payload.policyId.toString())
                writer.name("policyVersion").value(payload.policyVersion)
            }
            is CommandPayload.InstallRelease -> writer.name("releaseId").value(payload.releaseId.toString())
            CommandPayload.RequestSync -> Unit
        }
        writer.endObject()
        writer.name("issuedAt").value(command.issuedAt.toString())
        writer.name("expiresAt").value(command.expiresAt.toString())
        writer.name("correlationId").value(command.correlationId.toString())
    }

    fun event(command: CommandMessage, eventId: UUID, state: ReceiptState, recordedAt: Instant): String = json { writer ->
        writer.name("schemaVersion").value(1L)
        writer.name("eventId").value(eventId.toString())
        writer.name("commandId").value(command.commandId.toString())
        writer.name("deviceId").value(command.deviceId.toString())
        writer.name("correlationId").value(command.correlationId.toString())
        writer.name("recordedAt").value(recordedAt.toString())
        writer.name("state").value(state.wireName)
    }

    private fun json(write: (JsonWriter) -> Unit): String {
        val buffer = Buffer()
        JsonWriter.of(buffer).use { writer ->
            writer.beginObject()
            write(writer)
            writer.endObject()
        }
        return buffer.readUtf8()
    }
}