package dev.fleet.agent

import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Clock
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

@RunWith(AndroidJUnit4::class)
class CommandJournalInstrumentedTest {
    @Test fun receiptAndEventSurviveReopenAndDuplicateDelivery() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val name = "test-command-${UUID.randomUUID()}.db"
        val deviceId = UUID.randomUUID()
        val commandId = UUID.randomUUID()
        val command = command(deviceId, commandId)
        var database = Room.databaseBuilder(context, CommandDatabase::class.java, name).build()
        try {
            val journal = CommandJournal(database, deviceId, Clock.fixed(Instant.parse("2026-10-17T10:00:00Z"), ZoneOffset.UTC))
            val first = journal.receive(command.toByteArray())
            assertFalse(first.duplicate)
            assertEquals(ReceiptState.RECEIVED, first.state)
            val event = journal.pendingEvents().single()
            assertEquals(first.eventId.toString(), event.eventId)
            database.close()
            database = Room.databaseBuilder(context, CommandDatabase::class.java, name).build()
            val reopened = CommandJournal(database, deviceId, Clock.fixed(Instant.parse("2026-10-17T12:00:00Z"), ZoneOffset.UTC))
            val retry = reopened.receive(("  " + command).toByteArray())
            assertTrue(retry.duplicate)
            assertEquals(first.copy(duplicate = true), retry)
            assertEquals(listOf(event), reopened.pendingEvents())
        } finally {
            database.close()
            context.deleteDatabase(name)
        }
    }

    @Test fun commandIdentityAndSequenceConflictsDoNotChangeStoredReceipt() = withDatabase { database, _ ->
        val deviceId = UUID.randomUUID()
        val commandId = UUID.randomUUID()
        val journal = journal(database, deviceId)
        val original = command(deviceId, commandId)
        val first = journal.receive(original.toByteArray())
        val events = journal.pendingEvents()
        for (conflict in listOf(
            original.replace("REQUEST_SYNC", "INSTALL_RELEASE").replace("{}", """{"releaseId":"${UUID.randomUUID()}"}"""),
            original.replace("\"sequence\":1", "\"sequence\":2"),
            original.replace("374823b7-0e54-4893-b661-e638cc03310c", UUID.randomUUID().toString()),
            command(deviceId, UUID.randomUUID()),
            command(UUID.randomUUID(), UUID.randomUUID(), 2),
        )) {
            assertThrows(Exception::class.java) { journal.receive(conflict.toByteArray()) }
            assertEquals(events, journal.pendingEvents())
        }
        val normalized = original.replace("\"sequence\":1", "\"sequence\":1.0")
            .replace("09:00:00Z", "11:00:00+02:00")
        assertEquals(first.copy(duplicate = true), journal.receive(normalized.toByteArray()))
        journal.receive(command(deviceId, UUID.randomUUID(), 3).toByteArray())
        journal.receive(command(deviceId, UUID.randomUUID(), 2).toByteArray())
        assertEquals(3, journal.pendingEvents().size)
    }

    @Test fun failedOutboxInsertRollsBackInboxAndRetryCreatesOneReceipt() = withDatabase { database, _ ->
        val deviceId = UUID.randomUUID()
        val commandId = UUID.randomUUID()
        val journal = journal(database, deviceId)
        database.openHelper.writableDatabase.execSQL("CREATE TRIGGER test_outbox_failure BEFORE INSERT ON command_event_outbox BEGIN SELECT RAISE(ABORT, 'forced test failure'); END")
        assertThrows(Exception::class.java) { journal.receive(command(deviceId, commandId).toByteArray()) }
        assertNull(database.journal().command(commandId.toString()))
        assertNull(database.journal().sequence(deviceId.toString(), 1))
        assertTrue(journal.pendingEvents().isEmpty())
        database.openHelper.writableDatabase.execSQL("DROP TRIGGER test_outbox_failure")
        assertFalse(journal.receive(command(deviceId, commandId).toByteArray()).duplicate)
        assertEquals(1, journal.pendingEvents().size)
    }

    @Test fun concurrentRetriesAcrossDatabaseHandlesCommitOneReceipt() = withDatabase { database, name ->
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val second = Room.databaseBuilder(context, CommandDatabase::class.java, name).build()
        val executor = Executors.newFixedThreadPool(8)
        val start = CountDownLatch(1)
        try {
            val deviceId = UUID.randomUUID()
            val bytes = command(deviceId, UUID.randomUUID()).toByteArray()
            val journals = listOf(journal(database, deviceId), journal(second, deviceId))
            journals.forEach { it.pendingEvents() }
            val results = (0 until 8).map { index -> executor.submit<CommandReceipt> {
                check(start.await(10, TimeUnit.SECONDS))
                journals[index % journals.size].receive(bytes)
            } }
            start.countDown()
            val receipts = results.map { it.get(20, TimeUnit.SECONDS) }
            assertEquals(1, receipts.count { !it.duplicate })
            assertEquals(7, receipts.count { it.duplicate })
            assertEquals(1, receipts.map { it.eventId }.distinct().size)
            assertEquals(journals[0].pendingEvents(), journals[1].pendingEvents())
            assertEquals(1, journals[0].pendingEvents().size)
        } finally {
            start.countDown()
            executor.shutdownNow()
            assertTrue(executor.awaitTermination(10, TimeUnit.SECONDS))
            second.close()
        }
    }

    @Test fun exactExpiryCreatesAnExpiredEventWithoutExecution() = withDatabase { database, _ ->
        val deviceId = UUID.randomUUID()
        val journal = CommandJournal(database, deviceId,
            Clock.fixed(Instant.parse("2026-10-17T11:00:00Z"), ZoneOffset.UTC))
        val first = journal.receive(command(deviceId, UUID.randomUUID()).toByteArray())
        assertEquals(ReceiptState.EXPIRED, first.state)
        val event = org.json.JSONObject(journal.pendingEvents().single().wireJson)
        assertEquals(setOf("schemaVersion", "eventId", "commandId", "deviceId", "correlationId", "recordedAt", "state"),
            event.keys().asSequence().toSet())
        assertEquals("expired", event.getString("state"))
        assertEquals("2026-10-17T11:00:00Z", event.getString("recordedAt"))
        assertEquals(first.eventId.toString(), event.getString("eventId"))
        assertEquals(first.commandId.toString(), event.getString("commandId"))
        assertEquals(deviceId.toString(), event.getString("deviceId"))
    }

    @Test fun outboxReadsAreDeviceScopedBoundedAndNeverDeleteEvents() = withDatabase { database, _ ->
        val firstDevice = UUID.randomUUID()
        val secondDevice = UUID.randomUUID()
        val first = journal(database, firstDevice)
        val second = journal(database, secondDevice)
        first.receive(command(firstDevice, UUID.randomUUID()).toByteArray())
        first.receive(command(firstDevice, UUID.randomUUID(), 2).toByteArray())
        second.receive(command(secondDevice, UUID.randomUUID()).toByteArray())
        assertEquals(2, first.pendingEvents().size)
        assertEquals(1, first.pendingEvents(1).size)
        assertEquals(listOf(secondDevice.toString()), second.pendingEvents().map { it.deviceId })
        assertEquals(first.pendingEvents(), first.pendingEvents())
        for (limit in listOf(0, -1, 101)) assertThrows(Exception::class.java) { first.pendingEvents(limit) }
        assertEquals(2, first.pendingEvents().size)
        assertThrows(Exception::class.java) { second.receive(command(firstDevice, UUID.randomUUID(), 3).toByteArray()) }
        assertEquals(1, second.pendingEvents().size)
    }

    private fun journal(database: CommandDatabase, deviceId: UUID) = CommandJournal(database, deviceId,
        Clock.fixed(Instant.parse("2026-10-17T10:00:00Z"), ZoneOffset.UTC))

    private fun withDatabase(test: (CommandDatabase, String) -> Unit) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val name = "test-command-${UUID.randomUUID()}.db"
        val database = Room.databaseBuilder(context, CommandDatabase::class.java, name).build()
        try {
            test(database, name)
        } finally {
            database.close()
            context.deleteDatabase(name)
        }
    }

    private fun command(deviceId: UUID, commandId: UUID, sequence: Long = 1): String =
        """{"schemaVersion":1,"commandId":"$commandId","deviceId":"$deviceId","sequence":$sequence,"type":"REQUEST_SYNC","payload":{},"issuedAt":"2026-10-17T09:00:00Z","expiresAt":"2026-10-17T11:00:00Z","correlationId":"374823b7-0e54-4893-b661-e638cc03310c"}"""
}