package dev.fleet.agent

import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.tls.HandshakeCertificates
import okhttp3.tls.HeldCertificate
import org.junit.Assert.*
import org.junit.Test
import java.security.Principal
import java.util.UUID

class DeviceIdentityTransportTest {
    @Test fun clientKeyManagerDoesNotOfferWrongAlgorithmsIssuersOrServerKeys() {
        val root = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Device root").build()
        val leaf = HeldCertificate.Builder().ecdsa256().commonName("Device").signedBy(root).build()
        val manager = DeviceKeyManager(leaf.keyPair.private, listOf(leaf.certificate))
        assertNull(manager.chooseClientAlias(arrayOf("RSA"), null, null))
        assertNull(manager.chooseClientAlias(arrayOf("EC"), arrayOf<Principal>(javax.security.auth.x500.X500Principal("CN=Other")), null))
        val alias = manager.chooseClientAlias(arrayOf("EC"), arrayOf<Principal>(root.certificate.subjectX500Principal), null)
        assertNotNull(alias)
        assertSame(leaf.keyPair.private, manager.getPrivateKey(alias))
        assertNull(manager.getPrivateKey("other"))
        assertNull(manager.chooseServerAlias("EC", null, null))
    }

    @Test fun realTlsPresentsClientCertificateAndRejectsRedirectsMalformedIdentityAndOversize() {
        val root = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Transport root").build()
        val serverCertificate = HeldCertificate.Builder().ecdsa256().commonName("localhost").addSubjectAlternativeName("localhost").signedBy(root).build()
        val leaf = HeldCertificate.Builder().ecdsa256().commonName("Device").signedBy(root).build()
        val serverTls = HandshakeCertificates.Builder().heldCertificate(serverCertificate).addTrustedCertificate(root.certificate).build()
        MockWebServer().use { server ->
            server.useHttps(serverTls.sslSocketFactory(), false)
            server.requireClientAuth()
            server.start()
            val deviceId = UUID.randomUUID()
            val identity = DeviceTlsIdentity(server.url("/api/v1/device/identity").toUri(), deviceId,
                DeviceKeyManager(leaf.keyPair.private, listOf(leaf.certificate)))
            fun respond(body: String) = server.enqueue(MockResponse().setHeader("Content-Type", "application/json").setBody(body))
            respond("{\"schemaVersion\":1,\"deviceId\":\"$deviceId\"}")
            assertEquals(deviceId, DeviceIdentityTransport.check(identity, root.certificate))
            val request = server.takeRequest()
            assertNull(request.getHeader("Authorization"))
            assertEquals(leaf.certificate, request.handshake!!.peerCertificates.single())
            val wrongHost = DeviceTlsIdentity(java.net.URI(identity.endpoint.toString().replace("localhost", "127.0.0.1")),
                deviceId, identity.keyManager)
            assertThrows(javax.net.ssl.SSLException::class.java) { DeviceIdentityTransport.check(wrongHost, root.certificate) }
            val unrelatedRoot = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Untrusted root").build()
            assertThrows(javax.net.ssl.SSLException::class.java) { DeviceIdentityTransport.check(identity, unrelatedRoot.certificate) }
            for (body in listOf("{\"schemaVersion\":1,\"deviceId\":\"${UUID.randomUUID()}\"}",
                "{\"schemaVersion\":1,\"schemaVersion\":1,\"deviceId\":\"$deviceId\"}",
                "{\"schemaVersion\":\"1\",\"deviceId\":\"$deviceId\"}",
                "{\"schemaVersion\":1,\"deviceId\":\"$deviceId\",\"extra\":true}", " ".repeat(4097))) {
                respond(body)
                assertThrows(Exception::class.java) { DeviceIdentityTransport.check(identity, root.certificate) }
            }
            server.enqueue(MockResponse().setResponseCode(302).setHeader("Location", server.url("/unexpected")))
            assertEquals(302, assertThrows(DeviceHttpException::class.java) {
                DeviceIdentityTransport.check(identity, root.certificate)
            }.status)
        }
    }
}