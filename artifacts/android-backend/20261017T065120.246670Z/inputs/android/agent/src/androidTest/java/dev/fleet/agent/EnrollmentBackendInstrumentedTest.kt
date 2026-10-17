package dev.fleet.agent

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayInputStream
import java.io.File
import java.security.KeyStore
import java.security.Signature
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.time.Instant
import java.util.Base64
import java.util.UUID

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
                .put("csrDer", csr).put("responseBase64", Base64.getEncoder().encodeToString(first))
                .put("challengeBase64", Base64.getEncoder().encodeToString(challenge))
                .put("signatureBase64", Base64.getEncoder().encodeToString(signature)).toString())
        } finally {
            namespaces.forEach { current ->
                val alias = "fleet-enrollment-$current"
                if (keyStore.containsAlias(alias)) keyStore.deleteEntry(alias)
                File(context.noBackupFilesDir, "enrollment-$current").deleteRecursively()
            }
        }
    }
}