package dev.fleet.agent

import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.hivemq.client.mqtt.datatypes.MqttQos
import com.hivemq.client.mqtt.mqtt3.Mqtt3AsyncClient
import com.hivemq.client.mqtt.mqtt3.exceptions.Mqtt3SubAckException
import com.hivemq.client.mqtt.mqtt3.message.subscribe.suback.Mqtt3SubAckReturnCode
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayInputStream
import java.io.File
import java.security.KeyStore
import java.security.KeyFactory
import android.security.keystore.KeyInfo
import android.security.keystore.KeyProperties
import java.security.Signature
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.time.Instant
import java.util.Base64
import java.util.UUID
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ExecutionException
import java.util.concurrent.TimeUnit

@RunWith(AndroidJUnit4::class)
class EnrollmentBackendInstrumentedTest {
    @Test fun realHttpsEnrollmentPersistsAndRecoversTheKeystoreIdentity() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val input = File(context.filesDir, "enrollment-integration-input.json")
        val output = File(context.filesDir, "enrollment-integration-result.json")
        val recoveryFile = File(context.filesDir, "enrollment-event-recovery.json")
        val phase = InstrumentationRegistry.getArguments().getString("recoveryPhase")
        require(phase == null || phase == "prepare" || phase == "resume")
        val resuming = phase == "resume"
        val state = if (resuming) JSONObject(recoveryFile.readText()) else null
        if (!resuming) require(input.length() in 1..100000)
        val configuration = state?.getJSONObject("configuration") ?: JSONObject(input.readText())
        if (!resuming) require(input.delete())
        require(!output.exists())
        val namespace = configuration.getString("namespace")
        require(namespace.matches(Regex("integration-[0-9a-f-]{36}")))
        val namespaces = listOf(namespace, "$namespace-conflict")
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        if (!resuming) require(namespaces.none { keyStore.containsAlias("fleet-enrollment-$it") })
        var mqtt: Mqtt3AsyncClient? = null
        val databaseName = state?.getString("databaseName") ?: "test-mqtt-command-${UUID.randomUUID()}.db"
        require(databaseName.matches(Regex("test-mqtt-command-[0-9a-f-]{36}\\.db")))
        var checkpointRetained = false
        var database: CommandDatabase? = null
        var mqttReceipt: JSONObject? = null
        var eventAcknowledgement: JSONObject? = null
        var retainedEventId: String? = null
        val eventDeletion = configuration.optBoolean("eventDeletion", false)
        val eventWorker = configuration.optBoolean("eventWorker", false)
        var eventWorkerRetryVerified = state?.getBoolean("workerRetryVerified") ?: false
        var eventWorkerVerified = false
        var eventWorkerDisabledRetained = false
        var eventDeletionVerified = false
        var eventDeleteRollbackVerified = false
        var eventInboxRetained = false
        var disabledEventRetained = false
        fun root(name: String): X509Certificate = CertificateFactory.getInstance("X.509")
            .generateCertificate(ByteArrayInputStream(Base64.getDecoder().decode(configuration.getString(name)))) as X509Certificate
        try {
            val serverRoot = root("serverRootDer")
            val deviceRoot = root("deviceRootDer")
            if (phase == "prepare") {
                require(eventWorker && eventDeletion)
                check(!recoveryFile.exists())
                val publicConfiguration = JSONObject(configuration.toString())
                publicConfiguration.remove("bootstrap")
                recoveryFile.writeText(JSONObject().put("configuration", publicConfiguration)
                    .put("databaseName", databaseName).put("preparedProcessId", android.os.Process.myPid()).toString())
            }
            val identity = AndroidEnrollmentIdentity(context, namespace)
            val alias = "fleet-enrollment-$namespace"
            var csr: String
            var first: ByteArray
            if (resuming) {
                assertNotEquals(state!!.getInt("preparedProcessId"), android.os.Process.myPid())
                assertTrue(eventWorkerRetryVerified)
                csr = state.getString("csrDer")
                first = Base64.getDecoder().decode(state.getString("responseBase64"))
                assertTrue(identity.restore(deviceRoot, Instant.now()))
            } else {
                val bootstrap = EnrollmentBootstrap.parse(configuration.getJSONObject("bootstrap").toString().toByteArray())
                csr = identity.prepare(bootstrap, serverRoot, deviceRoot)
                assertNull(keyStore.getKey(alias, null).encoded)
                val keyInfo = KeyFactory.getInstance("EC", "AndroidKeyStore")
                    .getKeySpec(keyStore.getKey(alias, null), KeyInfo::class.java)
                assertTrue(keyInfo.digests.contains(KeyProperties.DIGEST_NONE))
                val rejected = assertThrows(EnrollmentHttpException::class.java) {
                    EnrollmentTransport.redeem(EnrollmentBootstrap(bootstrap.endpoint, bootstrap.pendingId,
                        if (bootstrap.token == "A".repeat(43)) "B" + "A".repeat(42) else "A".repeat(43)), csr, serverRoot)
                }
                assertEquals(401, rejected.status)
                first = EnrollmentTransport.redeem(bootstrap, csr, serverRoot)
                identity.complete(first, deviceRoot, Instant.now())
                val reopened = AndroidEnrollmentIdentity(context, namespace)
                assertTrue(reopened.restore(deviceRoot, Instant.now()))
                assertEquals(csr, reopened.prepare(bootstrap, serverRoot, deviceRoot))
                val replay = EnrollmentTransport.redeem(bootstrap, csr, serverRoot)
                assertArrayEquals(first, replay)
                val conflictCsr = AndroidEnrollmentIdentity(context, namespaces.last()).prepare(bootstrap, serverRoot, deviceRoot)
                val conflict = assertThrows(EnrollmentHttpException::class.java) {
                    EnrollmentTransport.redeem(bootstrap, conflictCsr, serverRoot)
                }
                assertEquals(409, conflict.status)
            }
            val response = EnrollmentResponse.parse(first)
            val reopened = AndroidEnrollmentIdentity(context, namespace)
            val tlsIdentity = reopened.tlsIdentity(serverRoot, deviceRoot, Instant.now())
            if (phase == "prepare") {
                recoveryFile.writeText(JSONObject(recoveryFile.readText())
                    .put("csrDer", csr).put("responseBase64", Base64.getEncoder().encodeToString(first)).toString())
            }
            if (configuration.optBoolean("eventUpload", false)) {
                val journalDatabase = Room.databaseBuilder(context, CommandDatabase::class.java, databaseName).build()
                database = journalDatabase
                val now = Instant.now()
                val command = if (resuming) JSONObject(state!!.getString("commandJson")) else JSONObject().put("schemaVersion", 1)
                    .put("commandId", configuration.getString("eventCommandId"))
                    .put("correlationId", configuration.getString("eventCorrelationId"))
                    .put("deviceId", response.deviceId.toString()).put("sequence", 1)
                    .put("type", "REQUEST_SYNC").put("payload", JSONObject())
                    .put("issuedAt", now.toString()).put("expiresAt", now.plusSeconds(3600).toString())
                val journal = CommandJournal(journalDatabase, response.deviceId)
                if (resuming) {
                    assertEquals(state!!.getString("wireJson"), journal.pendingEvents().single().wireJson)
                    assertNotNull(journalDatabase.journal().command(command.getString("commandId")))
                }
                val receipt = journal.receive(command.toString().toByteArray())
                if (resuming) assertTrue(receipt.duplicate)
                val pending = journal.pendingEvents().single()
                assertEquals(receipt.eventId.toString(), pending.eventId)
                if (resuming) {
                    assertEquals(state!!.getString("wireJson"), pending.wireJson)
                    assertEquals(state.getString("eventId"), pending.eventId)
                } else {
                    if (eventWorker) {
                        assertEquals(androidx.work.ListenableWorker.Result.retry(),
                            runWorker(context, databaseName, tlsIdentity, serverRoot, 0))
                        eventWorkerRetryVerified = true
                    } else {
                        assertEquals(503, assertThrows(DeviceHttpException::class.java) {
                            if (eventDeletion) journal.uploadAndAcknowledge(tlsIdentity, serverRoot, pending)
                            else CommandEventTransport.upload(tlsIdentity, serverRoot, pending)
                        }.status)
                    }
                    assertEquals(pending, journal.pendingEvents().single())
                    if (phase == "prepare") {
                        val checkpoint = JSONObject(recoveryFile.readText()).put("commandJson", command.toString())
                            .put("wireJson", pending.wireJson).put("eventId", pending.eventId)
                            .put("workerRetryVerified", eventWorkerRetryVerified)
                        recoveryFile.writeText(checkpoint.toString())
                        File(context.filesDir, "enrollment-integration-prepared.json").writeText(JSONObject()
                            .put("schemaVersion", 1).put("preparedProcessId", android.os.Process.myPid())
                            .put("deviceId", response.deviceId.toString()).put("eventId", pending.eventId)
                            .put("wireSha256", java.security.MessageDigest.getInstance("SHA-256")
                                .digest(pending.wireJson.toByteArray()).joinToString("") { byte -> "%02x".format(byte) }).toString())
                        checkpointRetained = true
                        return
                    }
                }
                journalDatabase.close()
                val recoveredDatabase = Room.databaseBuilder(context, CommandDatabase::class.java, databaseName).build()
                database = recoveredDatabase
                val recoveredJournal = CommandJournal(recoveredDatabase, response.deviceId)
                val recoveredEvent = recoveredJournal.pendingEvents().single()
                assertEquals(pending, recoveredEvent)
                val acknowledgement = CommandEventTransport.upload(tlsIdentity, serverRoot, recoveredEvent)
                assertEquals(receipt.eventId, acknowledgement.eventId)
                assertEquals(response.deviceId, acknowledgement.deviceId)
                assertEquals(pending, recoveredJournal.pendingEvents().single())
                retainedEventId = pending.eventId
                eventAcknowledgement = JSONObject().put("schemaVersion", 1)
                    .put("deviceId", acknowledgement.deviceId.toString()).put("eventId", acknowledgement.eventId.toString())
                    .put("receivedAt", acknowledgement.receivedAt)
                if (eventDeletion) {
                    recoveredDatabase.openHelper.writableDatabase.execSQL("CREATE TRIGGER test_delete_failure AFTER DELETE ON command_event_outbox BEGIN SELECT RAISE(ABORT, 'forced delete failure'); END")
                    assertThrows(Exception::class.java) {
                        recoveredJournal.uploadAndAcknowledge(tlsIdentity, serverRoot, recoveredEvent)
                    }
                    assertEquals(listOf(pending), recoveredJournal.pendingEvents())
                    recoveredDatabase.close()
                    val retryDatabase = Room.databaseBuilder(context, CommandDatabase::class.java, databaseName).build()
                    database = retryDatabase
                    val retryJournal = CommandJournal(retryDatabase, response.deviceId)
                    assertEquals(listOf(pending), retryJournal.pendingEvents())
                    eventDeleteRollbackVerified = true
                    retryDatabase.openHelper.writableDatabase.execSQL("DROP TRIGGER test_delete_failure")
                    if (eventWorker) {
                        assertEquals(androidx.work.ListenableWorker.Result.success(
                            androidx.work.Data.Builder().putInt("delivered", 1).build()),
                            runWorker(context, databaseName, tlsIdentity, serverRoot, 1))
                        eventWorkerVerified = true
                    } else assertEquals(acknowledgement, retryJournal.uploadAndAcknowledge(tlsIdentity, serverRoot, pending))
                    assertTrue(retryJournal.pendingEvents().isEmpty())
                    retryDatabase.close()
                    val finalDatabase = Room.databaseBuilder(context, CommandDatabase::class.java, databaseName).build()
                    database = finalDatabase
                    val finalJournal = CommandJournal(finalDatabase, response.deviceId)
                    assertTrue(finalJournal.pendingEvents().isEmpty())
                    assertEquals(receipt.copy(duplicate = true), finalJournal.receive(command.toString().toByteArray()))
                    assertTrue(finalJournal.pendingEvents().isEmpty())
                    assertNotNull(finalDatabase.journal().command(receipt.commandId.toString()))
                    eventInboxRetained = true
                    eventDeletionVerified = true
                    command.put("commandId", UUID.randomUUID().toString()).put("sequence", 2)
                    finalJournal.receive(command.toString().toByteArray())
                }
            }
            if (configuration.has("mqttPort")) {
                val probe = configuration.getString("mqttProbe")
                require(probe.matches(Regex("probe-[0-9a-f-]{36}")))
                val client = DeviceMqttTransport.client(tlsIdentity, serverRoot, configuration.getInt("mqttPort"))
                mqtt = client
                client.connectWith().cleanSession(true).keepAlive(15).send().get(15, TimeUnit.SECONDS)
                val journalDatabase = Room.databaseBuilder(context, CommandDatabase::class.java, databaseName).build()
                database = journalDatabase
                val journal = CommandJournal(journalDatabase, response.deviceId)
                val received = CompletableFuture<List<CommandReceipt>>()
                val receipts = mutableListOf<CommandReceipt>()
                val subscribed = client.subscribeWith().topicFilter("fleet/devices/${response.deviceId}/commands")
                    .qos(MqttQos.AT_LEAST_ONCE).callback { message ->
                        try {
                            val receipt = journal.receiveFromMqtt(message.topic.toString(), message.payloadAsBytes, message.isRetain)
                            synchronized(receipts) {
                                receipts.add(receipt)
                                if (receipts.size == 2) received.complete(receipts.toList())
                            }
                        } catch (failure: Exception) {
                            received.completeExceptionally(failure)
                        }
                    }.send().get(15, TimeUnit.SECONDS)
                assertEquals(listOf(Mqtt3SubAckReturnCode.SUCCESS_MAXIMUM_QOS_1), subscribed.returnCodes)
                rejectedSubscription(client, "fleet/devices/${UUID.randomUUID()}/commands")
                rejectedSubscription(client, "fleet/#")
                client.publishWith().topic("fleet/devices/${response.deviceId}/command-events")
                    .qos(MqttQos.AT_LEAST_ONCE).payload(probe.toByteArray()).send().get(15, TimeUnit.SECONDS)
                val committed = received.get(15, TimeUnit.SECONDS)
                assertEquals(1, committed.count { !it.duplicate })
                assertEquals(1, committed.count { it.duplicate })
                assertEquals(1, committed.map { it.eventId }.distinct().size)
                assertTrue(committed.all { it.commandId == UUID.fromString(probe.removePrefix("probe-"))
                    && it.state == ReceiptState.RECEIVED })
                journalDatabase.close()
                val reopenedDatabase = Room.databaseBuilder(context, CommandDatabase::class.java, databaseName).build()
                database = reopenedDatabase
                val events = CommandJournal(reopenedDatabase, response.deviceId).pendingEvents()
                assertEquals(1, events.size)
                assertEquals(committed.first().eventId.toString(), events.single().eventId)
                val stored = requireNotNull(reopenedDatabase.journal().command(committed.first().commandId.toString()))
                assertEquals(response.deviceId.toString(), stored.deviceId)
                assertEquals("REQUEST_SYNC", JSONObject(stored.canonicalJson).getString("type"))
                mqttReceipt = JSONObject(events.single().wireJson)
            }
            assertEquals(response.deviceId, DeviceIdentityTransport.check(tlsIdentity, serverRoot))
            assertEquals(401, assertThrows(DeviceHttpException::class.java) {
                DeviceIdentityTransport.check(tlsIdentity, serverRoot)
            }.status)
            if (eventAcknowledgement != null) {
                val journal = CommandJournal(requireNotNull(database), response.deviceId)
                val retained = journal.pendingEvents().single()
                assertEquals(401, assertThrows(DeviceHttpException::class.java) {
                    if (eventDeletion) journal.uploadAndAcknowledge(tlsIdentity, serverRoot, retained)
                    else CommandEventTransport.upload(tlsIdentity, serverRoot, retained)
                }.status)
                assertEquals(retained, journal.pendingEvents().single())
                disabledEventRetained = true
                if (eventWorker) {
                    assertEquals(androidx.work.ListenableWorker.Result.failure(),
                        runWorker(context, databaseName, tlsIdentity, serverRoot, 0))
                    assertEquals(listOf(retained), journal.pendingEvents())
                    eventWorkerDisabledRetained = true
                }
            }
            mqtt?.let { client ->
                rejectedSubscription(client, "fleet/devices/${response.deviceId}/commands")
                client.disconnect().get(10, TimeUnit.SECONDS)
                val reconnect = DeviceMqttTransport.client(tlsIdentity, serverRoot, configuration.getInt("mqttPort"))
                mqtt = reconnect
                assertThrows(ExecutionException::class.java) {
                    reconnect.connectWith().cleanSession(true).send().get(15, TimeUnit.SECONDS)
                }
            }
            assertArrayEquals(Base64.getDecoder().decode(response.certificateDer), keyStore.getCertificate(alias).encoded)
            assertNull(keyStore.getKey(alias, null).encoded)
            val challenge = UUID.randomUUID().toString().toByteArray()
            val signature = Signature.getInstance("SHA256withECDSA").run {
                initSign(keyStore.getKey(alias, null) as java.security.PrivateKey)
                update(challenge)
                sign()
            }
            assertTrue(Signature.getInstance("SHA256withECDSA").run {
                initVerify(keyStore.getCertificate(alias).publicKey)
                update(challenge)
                verify(signature)
            })
            output.writeText(JSONObject().put("schemaVersion", 1).put("deviceId", response.deviceId.toString())
                .put("eventProcessRecoveryVerified", resuming)
                .put("preparedProcessId", state?.getInt("preparedProcessId") ?: android.os.Process.myPid())
                .put("recoveredProcessId", android.os.Process.myPid())
                .put("recoveredWireSha256", if (resuming) java.security.MessageDigest.getInstance("SHA-256")
                    .digest(state!!.getString("wireJson").toByteArray()).joinToString("") { byte -> "%02x".format(byte) } else "")
                .put("mtlsAuthenticated", true).put("disabledRejected", true)
                .put("mqttAuthenticated", configuration.has("mqttPort"))
                .put("mqttDisabledRejected", configuration.has("mqttPort"))
                .put("mqttJournalVerified", mqttReceipt != null).put("mqttReceipt", mqttReceipt)
                .put("eventUploadVerified", eventAcknowledgement != null).put("eventAcknowledgement", eventAcknowledgement)
                .put("retainedEventId", retainedEventId)
                .put("eventDeletionVerified", eventDeletionVerified).put("eventDeleteRollbackVerified", eventDeleteRollbackVerified)
                .put("eventInboxRetained", eventInboxRetained).put("disabledEventRetained", disabledEventRetained)
                .put("eventWorkerVerified", eventWorkerVerified).put("eventWorkerRetryVerified", eventWorkerRetryVerified)
                .put("eventWorkerDisabledRetained", eventWorkerDisabledRetained)
                .put("csrDer", csr).put("responseBase64", Base64.getEncoder().encodeToString(first))
                .put("challengeBase64", Base64.getEncoder().encodeToString(challenge))
                .put("signatureBase64", Base64.getEncoder().encodeToString(signature)).toString())
        } finally {
            try { mqtt?.disconnect()?.get(5, TimeUnit.SECONDS) } catch (_: Exception) { }
            database?.close()
            if (!checkpointRetained && phase != null) cleanupOnlyEventRecoveryFixture()
            if (phase == null) {
                context.deleteDatabase(databaseName)
                namespaces.forEach { current ->
                    val alias = "fleet-enrollment-$current"
                    if (keyStore.containsAlias(alias)) keyStore.deleteEntry(alias)
                    File(context.noBackupFilesDir, "enrollment-$current").deleteRecursively()
                }
            }
        }
    }

    @Test fun cleanupOnlyEventRecoveryFixture() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val file = File(context.filesDir, "enrollment-event-recovery.json")
        if (file.exists()) {
            val state = JSONObject(file.readText())
            val namespace = state.getJSONObject("configuration").getString("namespace")
            require(namespace.matches(Regex("integration-[0-9a-f-]{36}")))
            val databaseName = state.getString("databaseName")
            require(databaseName.matches(Regex("test-mqtt-command-[0-9a-f-]{36}\\.db")))
            val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
            listOf(namespace, "$namespace-conflict").forEach { current ->
                val alias = "fleet-enrollment-$current"
                if (keyStore.containsAlias(alias)) keyStore.deleteEntry(alias)
                check(!keyStore.containsAlias(alias))
                check(File(context.noBackupFilesDir, "enrollment-$current").deleteRecursively())
            }
            context.deleteDatabase(databaseName)
            check(!context.getDatabasePath(databaseName).exists())
            check(file.delete())
        }
        val prepared = File(context.filesDir, "enrollment-integration-prepared.json")
        if (prepared.exists()) check(prepared.delete())
    }

    private fun runWorker(context: android.content.Context, databaseName: String, identity: DeviceTlsIdentity,
        serverRoot: X509Certificate, attempt: Int): androidx.work.ListenableWorker.Result =
        androidx.work.testing.TestWorkerBuilder.from(context, EventUploadWorker::class.java,
            java.util.concurrent.Executor { task -> task.run() }).setRunAttemptCount(attempt)
            .setWorkerFactory(object : androidx.work.WorkerFactory() {
                override fun createWorker(appContext: android.content.Context, workerClassName: String,
                    workerParameters: androidx.work.WorkerParameters): androidx.work.ListenableWorker =
                    EventUploadWorker(appContext, workerParameters) {
                        EventDeliverySession(Room.databaseBuilder(context, CommandDatabase::class.java, databaseName).build(),
                            identity, serverRoot)
                    }
            }).build().doWork()

    private fun rejectedSubscription(client: Mqtt3AsyncClient, topic: String) {
        try {
            val result = client.subscribeWith().topicFilter(topic).qos(MqttQos.AT_LEAST_ONCE).send().get(15, TimeUnit.SECONDS)
            assertEquals(listOf(Mqtt3SubAckReturnCode.FAILURE), result.returnCodes)
        } catch (failure: ExecutionException) {
            assertTrue(failure.cause is Mqtt3SubAckException)
            val rejected = failure.cause as Mqtt3SubAckException
            assertEquals(listOf(Mqtt3SubAckReturnCode.FAILURE), rejected.mqttMessage.returnCodes)
        }
    }
}