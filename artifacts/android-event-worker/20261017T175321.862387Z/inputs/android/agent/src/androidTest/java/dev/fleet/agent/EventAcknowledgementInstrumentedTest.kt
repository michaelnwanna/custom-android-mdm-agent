package dev.fleet.agent

import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.Dispatcher
import okhttp3.mockwebserver.RecordedRequest
import okhttp3.tls.HandshakeCertificates
import okhttp3.tls.HeldCertificate
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Instant
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

@RunWith(AndroidJUnit4::class)
class EventAcknowledgementInstrumentedTest {
    @Test fun matchingAuthenticatedReplyDeletesOnlyItsEventAndDuplicateDoesNotRecreateIt() = withLab { lab ->
        val command = lab.command(UUID.randomUUID(), 1)
        val receipt = lab.journal().receive(command.toByteArray())
        lab.journal().receive(lab.command(UUID.randomUUID(), 2).toByteArray())
        val otherDevice = UUID.randomUUID()
        CommandJournal(lab.database, otherDevice).receive(lab.command(UUID.randomUUID(), 1, otherDevice).toByteArray())
        val event = lab.journal().pendingEvents().first()
        lab.reply(event)
        val acknowledgement = lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event)
        assertEquals(receipt.eventId, acknowledgement.eventId)
        assertEquals(1, lab.journal().pendingEvents().size)
        assertEquals(1, CommandJournal(lab.database, otherDevice).pendingEvents().size)
        assertNotNull(lab.database.journal().command(receipt.commandId.toString()))
        lab.reopen()
        assertEquals(receipt.copy(duplicate = true), lab.journal().receive(command.toByteArray()))
        assertEquals(1, lab.journal().pendingEvents().size)
        assertNull(lab.database.journal().event(lab.identity.deviceId.toString(), event.eventId))
        assertThrows(Exception::class.java) {
            lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event)
        }
        assertEquals(1, lab.server.requestCount)
    }

    @Test fun untrustedMalformedMismatchedAndErrorRepliesLeaveOriginalRowUnchanged() = withLab { lab ->
        lab.journal().receive(lab.command(UUID.randomUUID(), 1).toByteArray())
        val event = lab.journal().pendingEvents().single()
        val wrongRoot = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Other root").build()
        assertThrows(Exception::class.java) {
            lab.journal().uploadAndAcknowledge(lab.identity, wrongRoot.certificate, event)
        }
        val invalidBodies = listOf("{}", lab.ack(event).replace(event.eventId, UUID.randomUUID().toString()),
            lab.ack(event).replace(lab.identity.deviceId.toString(), UUID.randomUUID().toString()),
            lab.ack(event).replace("2026-10-17T12:00:00Z", "invalid"))
        for (body in invalidBodies) {
            lab.server.enqueue(MockResponse().setHeader("Content-Type", "application/json").setBody(body))
            assertThrows(Exception::class.java) { lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event) }
            assertEquals(listOf(event), lab.journal().pendingEvents())
        }
        for (status in listOf(401, 409, 429, 503)) {
            lab.server.enqueue(MockResponse().setResponseCode(status))
            assertEquals(status, assertThrows(DeviceHttpException::class.java) {
                lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event)
            }.status)
            assertEquals(listOf(event), lab.journal().pendingEvents())
        }
        val otherJournal = CommandJournal(lab.database, UUID.randomUUID())
        assertThrows(Exception::class.java) { otherJournal.uploadAndAcknowledge(lab.identity, lab.root.certificate, event) }
        assertThrows(Exception::class.java) {
            lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event.copy(wireJson = event.wireJson + " "))
        }
        lab.reopen()
        assertEquals(listOf(event), lab.journal().pendingEvents())
        assertEquals(8, lab.server.requestCount)
    }

    @Test fun deleteFailureRollsBackAndAuthenticatedRetryCanFinishAfterReopen() = withLab { lab ->
        lab.journal().receive(lab.command(UUID.randomUUID(), 1).toByteArray())
        val event = lab.journal().pendingEvents().single()
        lab.database.openHelper.writableDatabase.execSQL("CREATE TRIGGER test_delete_failure AFTER DELETE ON command_event_outbox BEGIN SELECT RAISE(ABORT, 'forced delete failure'); END")
        lab.reply(event)
        assertThrows(Exception::class.java) { lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event) }
        assertEquals(listOf(event), lab.journal().pendingEvents())
        lab.reopen()
        assertEquals(listOf(event), lab.journal().pendingEvents())
        lab.database.openHelper.writableDatabase.execSQL("DROP TRIGGER test_delete_failure")
        lab.reply(event)
        lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event)
        lab.reopen()
        assertTrue(lab.journal().pendingEvents().isEmpty())
        assertNotNull(lab.database.journal().command(event.commandId))
        assertEquals(2, lab.server.requestCount)
    }

    @Test fun overlappingAuthenticatedUploadsAcceptAnAlreadyDeletedEvent() = withLab { lab ->
        lab.journal().receive(lab.command(UUID.randomUUID(), 1).toByteArray())
        val event = lab.journal().pendingEvents().single()
        val requests = CountDownLatch(2)
        val executor = Executors.newFixedThreadPool(2)
        lab.server.dispatcher = object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse {
                requests.countDown()
                check(requests.await(10, TimeUnit.SECONDS))
                return MockResponse().setHeader("Content-Type", "application/json").setBody(lab.ack(event))
            }
        }
        try {
            val results = (0 until 2).map { executor.submit<EventAcknowledgement> {
                lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event)
            } }
            assertEquals(1, results.map { it.get(20, TimeUnit.SECONDS) }.distinct().size)
            lab.reopen()
            assertTrue(lab.journal().pendingEvents().isEmpty())
            assertNotNull(lab.database.journal().command(event.commandId))
            assertEquals(2, lab.server.requestCount)
        } finally {
            repeat(2) { requests.countDown() }
            executor.shutdownNow()
            assertTrue(executor.awaitTermination(10, TimeUnit.SECONDS))
        }
    }

    @Test fun rowChangedDuringUploadIsNotDeletedByTheEarlierAcknowledgement() = withLab { lab ->
        lab.journal().receive(lab.command(UUID.randomUUID(), 1).toByteArray())
        val event = lab.journal().pendingEvents().single()
        val requestReceived = CountDownLatch(1)
        val respond = CountDownLatch(1)
        val executor = Executors.newSingleThreadExecutor()
        lab.server.dispatcher = object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse {
                requestReceived.countDown()
                check(respond.await(10, TimeUnit.SECONDS))
                return MockResponse().setHeader("Content-Type", "application/json").setBody(lab.ack(event))
            }
        }
        try {
            val upload = executor.submit<EventAcknowledgement> {
                lab.journal().uploadAndAcknowledge(lab.identity, lab.root.certificate, event)
            }
            assertTrue(requestReceived.await(10, TimeUnit.SECONDS))
            val changed = event.copy(wireJson = event.wireJson + " ")
            lab.database.openHelper.writableDatabase.execSQL(
                "UPDATE command_event_outbox SET wireJson = ? WHERE eventId = ?", arrayOf(changed.wireJson, event.eventId))
            respond.countDown()
            assertTrue(assertThrows(ExecutionException::class.java) { upload.get(20, TimeUnit.SECONDS) }
                .cause is IllegalArgumentException)
            lab.reopen()
            assertEquals(listOf(changed), lab.journal().pendingEvents())
        } finally {
            respond.countDown()
            executor.shutdownNow()
            assertTrue(executor.awaitTermination(10, TimeUnit.SECONDS))
        }
    }

    private fun withLab(action: (Lab) -> Unit) {
        val lab = Lab()
        try { action(lab) } finally { lab.close() }
    }

    private class Lab {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val name = "test-event-ack-${UUID.randomUUID()}.db"
        var database = Room.databaseBuilder(context, CommandDatabase::class.java, name).build()
        val root = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Test event root").build()
        val server = MockWebServer()
        val identity: DeviceTlsIdentity

        init {
            val serverCertificate = HeldCertificate.Builder().ecdsa256().commonName("localhost")
                .addSubjectAlternativeName("localhost").signedBy(root).build()
            val leaf = HeldCertificate.Builder().ecdsa256().commonName("Test device").signedBy(root).build()
            val tls = HandshakeCertificates.Builder().heldCertificate(serverCertificate)
                .addTrustedCertificate(root.certificate).build()
            server.useHttps(tls.sslSocketFactory(), false)
            server.requireClientAuth()
            server.start()
            identity = DeviceTlsIdentity(server.url("/api/v1/device/identity").toUri(), UUID.randomUUID(),
                DeviceKeyManager(leaf.keyPair.private, listOf(leaf.certificate)))
        }

        fun journal() = CommandJournal(database, identity.deviceId)
        fun reopen() {
            database.close()
            database = Room.databaseBuilder(context, CommandDatabase::class.java, name).build()
        }
        fun command(commandId: UUID, sequence: Long, deviceId: UUID = identity.deviceId): String {
            val now = Instant.now()
            return """{"schemaVersion":1,"commandId":"$commandId","deviceId":"$deviceId","sequence":$sequence,"type":"REQUEST_SYNC","payload":{},"issuedAt":"$now","expiresAt":"${now.plusSeconds(3600)}","correlationId":"${UUID.randomUUID()}"}"""
        }
        fun ack(event: CommandEventRow) =
            """{"schemaVersion":1,"deviceId":"${identity.deviceId}","eventId":"${event.eventId}","receivedAt":"2026-10-17T12:00:00Z"}"""
        fun reply(event: CommandEventRow) {
            server.enqueue(MockResponse().setHeader("Content-Type", "application/json").setBody(ack(event)))
        }
        fun close() {
            database.close()
            server.close()
            context.deleteDatabase(name)
        }
    }
}