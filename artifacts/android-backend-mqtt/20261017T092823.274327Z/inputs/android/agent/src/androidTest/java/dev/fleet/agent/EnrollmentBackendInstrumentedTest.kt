package dev.fleet.agent

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
        require(input.length() in 1..100000)
        val configuration = JSONObject(input.readText())
        require(input.delete())
        require(!output.exists())
        val namespace = configuration.getString("namespace")
        require(namespace.matches(Regex("integration-[0-9a-f-]{36}")))
        val namespaces = listOf(namespace, "$namespace-conflict")
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        require(namespaces.none { keyStore.containsAlias("fleet-enrollment-$it") })
        var mqtt: Mqtt3AsyncClient? = null
        fun root(name: String): X509Certificate = CertificateFactory.getInstance("X.509")
            .generateCertificate(ByteArrayInputStream(Base64.getDecoder().decode(configuration.getString(name)))) as X509Certificate
        try {
            val serverRoot = root("serverRootDer")
            val deviceRoot = root("deviceRootDer")
            val bootstrap = EnrollmentBootstrap.parse(configuration.getJSONObject("bootstrap").toString().toByteArray())
            val identity = AndroidEnrollmentIdentity(context, namespace)
            val csr = identity.prepare(bootstrap, serverRoot, deviceRoot)
            val alias = "fleet-enrollment-$namespace"
            assertNull(keyStore.getKey(alias, null).encoded)
            val keyInfo = KeyFactory.getInstance("EC", "AndroidKeyStore")
                .getKeySpec(keyStore.getKey(alias, null), KeyInfo::class.java)
            assertTrue(keyInfo.digests.contains(KeyProperties.DIGEST_NONE))
            val rejected = assertThrows(EnrollmentHttpException::class.java) {
                EnrollmentTransport.redeem(EnrollmentBootstrap(bootstrap.endpoint, bootstrap.pendingId,
                    if (bootstrap.token == "A".repeat(43)) "B" + "A".repeat(42) else "A".repeat(43)), csr, serverRoot)
            }
            assertEquals(401, rejected.status)
            val first = EnrollmentTransport.redeem(bootstrap, csr, serverRoot)
            val response = EnrollmentResponse.parse(first)
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
            val tlsIdentity = reopened.tlsIdentity(serverRoot, deviceRoot, Instant.now())
            if (configuration.has("mqttPort")) {
                val probe = configuration.getString("mqttProbe")
                require(probe.matches(Regex("probe-[0-9a-f-]{36}")))
                val client = DeviceMqttTransport.client(tlsIdentity, serverRoot, configuration.getInt("mqttPort"))
                mqtt = client
                client.connectWith().cleanSession(true).keepAlive(15).send().get(15, TimeUnit.SECONDS)
                val received = CompletableFuture<String>()
                val subscribed = client.subscribeWith().topicFilter("fleet/devices/${response.deviceId}/commands")
                    .qos(MqttQos.AT_LEAST_ONCE).callback { message ->
                        if (message.payloadAsBytes.contentEquals(probe.toByteArray())) received.complete(probe)
                        else received.completeExceptionally(IllegalStateException("Unexpected MQTT probe"))
                    }.send().get(15, TimeUnit.SECONDS)
                assertEquals(listOf(Mqtt3SubAckReturnCode.SUCCESS_MAXIMUM_QOS_1), subscribed.returnCodes)
                rejectedSubscription(client, "fleet/devices/${UUID.randomUUID()}/commands")
                rejectedSubscription(client, "fleet/#")
                client.publishWith().topic("fleet/devices/${response.deviceId}/command-events")
                    .qos(MqttQos.AT_LEAST_ONCE).payload(probe.toByteArray()).send().get(15, TimeUnit.SECONDS)
                assertEquals(probe, received.get(15, TimeUnit.SECONDS))
            }
            assertEquals(response.deviceId, DeviceIdentityTransport.check(tlsIdentity, serverRoot))
            assertEquals(401, assertThrows(DeviceHttpException::class.java) {
                DeviceIdentityTransport.check(tlsIdentity, serverRoot)
            }.status)
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
                .put("mtlsAuthenticated", true).put("disabledRejected", true)
                .put("mqttAuthenticated", configuration.has("mqttPort"))
                .put("mqttDisabledRejected", configuration.has("mqttPort"))
                .put("csrDer", csr).put("responseBase64", Base64.getEncoder().encodeToString(first))
                .put("challengeBase64", Base64.getEncoder().encodeToString(challenge))
                .put("signatureBase64", Base64.getEncoder().encodeToString(signature)).toString())
        } finally {
            try { mqtt?.disconnect()?.get(5, TimeUnit.SECONDS) } catch (_: Exception) { }
            namespaces.forEach { current ->
                val alias = "fleet-enrollment-$current"
                if (keyStore.containsAlias(alias)) keyStore.deleteEntry(alias)
                File(context.noBackupFilesDir, "enrollment-$current").deleteRecursively()
            }
        }
    }

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