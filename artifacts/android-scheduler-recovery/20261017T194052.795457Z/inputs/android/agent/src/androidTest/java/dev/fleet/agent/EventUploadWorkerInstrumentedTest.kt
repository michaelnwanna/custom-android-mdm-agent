package dev.fleet.agent

import android.content.Context
import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.work.BackoffPolicy
import androidx.work.ListenableWorker
import androidx.work.NetworkType
import androidx.work.WorkerFactory
import androidx.work.WorkerParameters
import androidx.work.testing.TestWorkerBuilder
import okhttp3.mockwebserver.Dispatcher
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.RecordedRequest
import okhttp3.tls.HandshakeCertificates
import okhttp3.tls.HeldCertificate
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Instant
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

@RunWith(AndroidJUnit4::class)
class EventUploadWorkerInstrumentedTest {
    @Test fun boundedBatchContinuesWithoutRecreatingAcknowledgedReceipts() = withLab { lab ->
        repeat(21) { index -> lab.receive(index + 1) }
        lab.server.dispatcher = object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse = lab.reply(JSONObject(request.body.readUtf8()).getString("eventId"))
        }
        assertEquals(ListenableWorker.Result.retry(), lab.worker().doWork())
        assertEquals(20, lab.server.requestCount)
        assertEquals(1, lab.journal.pendingEvents().size)
        val completed = lab.worker(1).doWork()
        assertEquals(ListenableWorker.Result.success(androidx.work.Data.Builder().putInt("delivered", 1).build()), completed)
        assertTrue(lab.journal.pendingEvents().isEmpty())
        assertEquals(21, lab.server.requestCount)
        lab.database.openHelper.readableDatabase.query("SELECT count(*) FROM command_inbox").use { cursor ->
            assertTrue(cursor.moveToFirst())
            assertEquals(21, cursor.getInt(0))
        }
    }

    @Test fun transientResponseRetainsExactEventForTheNextAttempt() = withLab { lab ->
        lab.receive(1)
        val event = lab.journal.pendingEvents().single()
        lab.server.enqueue(MockResponse().setResponseCode(503))
        assertEquals(ListenableWorker.Result.retry(), lab.worker().doWork())
        assertEquals(listOf(event), lab.journal.pendingEvents())
        lab.server.enqueue(lab.reply(event.eventId))
        assertEquals(ListenableWorker.Result.success(androidx.work.Data.Builder().putInt("delivered", 1).build()), lab.worker(1).doWork())
        assertTrue(lab.journal.pendingEvents().isEmpty())
        assertEquals(event.wireJson, lab.server.takeRequest(5, TimeUnit.SECONDS)!!.body.readUtf8())
        assertEquals(event.wireJson, lab.server.takeRequest(5, TimeUnit.SECONDS)!!.body.readUtf8())
    }

    @Test fun permanentOrExhaustedFailuresRetainEventsWithoutAnUnboundedRetry() = withLab { lab ->
        lab.receive(1)
        val event = lab.journal.pendingEvents().single()
        for (response in listOf(MockResponse().setResponseCode(401), MockResponse().setResponseCode(409),
            MockResponse().setHeader("Content-Type", "application/json").setBody("{}"))) {
            lab.server.enqueue(response)
            assertEquals(ListenableWorker.Result.failure(), lab.worker().doWork())
            assertEquals(listOf(event), lab.journal.pendingEvents())
        }
        lab.server.enqueue(MockResponse().setResponseCode(503))
        assertEquals(ListenableWorker.Result.failure(), lab.worker(4).doWork())
        assertEquals(ListenableWorker.Result.failure(), lab.worker(5).doWork())
        assertEquals(4, lab.server.requestCount)
        assertEquals(listOf(event), lab.journal.pendingEvents())
    }

    @Test fun stopCallbackDuringRequestCannotDeleteTheUnacknowledgedEvent() = withLab { lab ->
        lab.receive(1)
        val event = lab.journal.pendingEvents().single()
        val received = CountDownLatch(1)
        val respond = CountDownLatch(1)
        lab.server.dispatcher = object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse {
                received.countDown()
                check(respond.await(10, TimeUnit.SECONDS))
                return lab.reply(event.eventId)
            }
        }
        val worker = lab.worker()
        try {
            val delivery = lab.executor.submit<ListenableWorker.Result> { worker.doWork() }
            assertTrue(received.await(10, TimeUnit.SECONDS))
            worker.onStopped()
            respond.countDown()
            assertNotEquals(ListenableWorker.Result.success(), delivery.get(25, TimeUnit.SECONDS))
            assertEquals(listOf(event), lab.journal.pendingEvents())
            assertNotNull(lab.database.journal().command(event.commandId))
        } finally {
            respond.countDown()
        }
    }

    @Test fun workRequestHasConnectedConstraintBackoffAndNoIdentityInput() {
        val request = EventUploadScheduler.request()
        assertEquals(NetworkType.CONNECTED, request.workSpec.constraints.requiredNetworkType)
        assertEquals(BackoffPolicy.EXPONENTIAL, request.workSpec.backoffPolicy)
        assertEquals(30000L, request.workSpec.backoffDelayDuration)
        assertTrue(request.workSpec.input.keyValueMap.isEmpty())
    }

    @Test fun responseDeadlineRetainsTheEventBeforeTheSocketReadTimeout() = withLab { lab ->
        lab.receive(1)
        val event = lab.journal.pendingEvents().single()
        lab.server.enqueue(lab.reply(event.eventId).setBodyDelay(18, TimeUnit.SECONDS))
        val started = System.nanoTime()
        assertEquals(ListenableWorker.Result.retry(), lab.worker().doWork())
        val seconds = TimeUnit.NANOSECONDS.toSeconds(System.nanoTime() - started)
        assertTrue("Deadline must fire rather than an immediate failure", seconds >= 12)
        assertTrue("Controlled response must stop within the lab allowance", seconds <= 30)
        assertEquals(listOf(event), lab.journal.pendingEvents())
    }

    private fun withLab(action: (Lab) -> Unit) {
        val lab = Lab()
        try { action(lab) } finally { lab.close() }
    }

    private class Lab {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val name = "test-event-worker-${UUID.randomUUID()}.db"
        val database = Room.databaseBuilder(context, CommandDatabase::class.java, name).build()
        val executor = Executors.newFixedThreadPool(2)
        val root = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Worker root").build()
        val server = MockWebServer()
        val identity: DeviceTlsIdentity
        val journal: CommandJournal

        init {
            val certificate = HeldCertificate.Builder().ecdsa256().commonName("localhost")
                .addSubjectAlternativeName("localhost").signedBy(root).build()
            val leaf = HeldCertificate.Builder().ecdsa256().commonName("Worker device").signedBy(root).build()
            val tls = HandshakeCertificates.Builder().heldCertificate(certificate).addTrustedCertificate(root.certificate).build()
            server.useHttps(tls.sslSocketFactory(), false)
            server.requireClientAuth()
            server.start()
            identity = DeviceTlsIdentity(server.url("/api/v1/device/identity").toUri(), UUID.randomUUID(),
                DeviceKeyManager(leaf.keyPair.private, listOf(leaf.certificate)))
            journal = CommandJournal(database, identity.deviceId)
        }

        fun receive(sequence: Int) {
            val now = Instant.now()
            journal.receive("""{"schemaVersion":1,"commandId":"${UUID.randomUUID()}","deviceId":"${identity.deviceId}","sequence":$sequence,"type":"REQUEST_SYNC","payload":{},"issuedAt":"$now","expiresAt":"${now.plusSeconds(3600)}","correlationId":"${UUID.randomUUID()}"}""".toByteArray())
        }

        fun reply(eventId: String): MockResponse = MockResponse().setHeader("Content-Type", "application/json")
            .setBody("""{"schemaVersion":1,"deviceId":"${identity.deviceId}","eventId":"$eventId","receivedAt":"2026-10-17T12:00:00Z"}""")

        fun worker(attempt: Int = 0): EventUploadWorker = TestWorkerBuilder.from(context, EventUploadWorker::class.java, executor)
            .setRunAttemptCount(attempt).setWorkerFactory(object : WorkerFactory() {
                override fun createWorker(appContext: Context, workerClassName: String, workerParameters: WorkerParameters): ListenableWorker =
                    EventUploadWorker(appContext, workerParameters) {
                        EventDeliverySession(Room.databaseBuilder(context, CommandDatabase::class.java, name).build(), identity, root.certificate)
                    }
            }).build()

        fun close() {
            executor.shutdownNow()
            check(executor.awaitTermination(10, TimeUnit.SECONDS))
            database.close()
            server.close()
            context.deleteDatabase(name)
        }
    }
}