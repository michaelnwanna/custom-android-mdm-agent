package dev.fleet.agent

import okhttp3.tls.HeldCertificate
import okhttp3.tls.HandshakeCertificates
import org.junit.Assert.*
import org.junit.Test
import java.net.URI
import java.net.InetAddress
import java.security.KeyStore
import java.security.interfaces.ECPrivateKey
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.ExecutionException
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLSocket

class DeviceMqttTransportTest {
    @Test fun keyFactoryRetainsOnlyOriginalManagerAndCannotBeReinitialized() {
        val leaf = HeldCertificate.Builder().ecdsa256().commonName("Device").build()
        val manager = DeviceKeyManager(leaf.keyPair.private, listOf(leaf.certificate))
        val factory = DeviceKeyManagerFactory(manager)
        assertSame(manager, factory.keyManagers.single())
        assertThrows(UnsupportedOperationException::class.java) { factory.init(null as KeyStore?, null) }
        assertSame(manager, factory.keyManagers.single())
    }

    @Test fun mqttUsesEnrolledAuthorityUuidAndExplicitTlsWithoutReconnect() {
        val leaf = HeldCertificate.Builder().ecdsa256().commonName("Device").build()
        val deviceId = UUID.randomUUID()
        val identity = DeviceTlsIdentity(URI("https://localhost:8443/api/v1/device/identity"), deviceId,
            DeviceKeyManager(leaf.keyPair.private, listOf(leaf.certificate)))
        val client = DeviceMqttTransport.client(identity, leaf.certificate, 8884)
        assertEquals(deviceId.toString(), client.config.clientIdentifier.get().toString())
        assertEquals("localhost", client.config.serverHost)
        assertEquals(8884, client.config.serverPort)
        assertTrue(client.config.sslConfig.isPresent)
        assertFalse(client.config.automaticReconnect.isPresent)
        for (port in listOf(0, -1, 65536)) {
            assertThrows(IllegalArgumentException::class.java) { DeviceMqttTransport.client(identity, leaf.certificate, port) }
        }
        for (authority in listOf("http://localhost/", "https://user@localhost/", "https://localhost/?other=true")) {
            val invalid = DeviceTlsIdentity(URI(authority), deviceId, identity.keyManager)
            assertThrows(IllegalArgumentException::class.java) { DeviceMqttTransport.client(invalid, leaf.certificate, 8884) }
        }
    }

    @Test fun realHiveMqTlsUsesPrivateKeyHandleAndRejectsHostnameMismatch() {
        val root = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("MQTT root").build()
        val serverCertificate = HeldCertificate.Builder().ecdsa256().commonName("localhost")
            .addSubjectAlternativeName("localhost").signedBy(root).build()
        val leaf = HeldCertificate.Builder().ecdsa256().commonName("Device").signedBy(root).build()
        val handle = object : ECPrivateKey by (leaf.keyPair.private as ECPrivateKey) {
            override fun getEncoded(): ByteArray = throw AssertionError("Private key export attempted")
            override fun getFormat(): String? = null
        }
        val tls = HandshakeCertificates.Builder().heldCertificate(serverCertificate)
            .addTrustedCertificate(root.certificate).build()
        for (host in listOf("localhost", "127.0.0.1")) {
            (tls.sslContext().serverSocketFactory.createServerSocket(0, 1,
                InetAddress.getByName("127.0.0.1")) as SSLServerSocket).use { server ->
                server.needClientAuth = true
                server.soTimeout = 10000
                val executor = Executors.newSingleThreadExecutor()
                try {
                    val observed = executor.submit<Pair<java.security.cert.Certificate, Int>> {
                        (server.accept() as SSLSocket).use { socket ->
                            socket.soTimeout = 10000
                            socket.startHandshake()
                            Pair(socket.session.peerCertificates.single(), socket.inputStream.read())
                        }
                    }
                    val identity = DeviceTlsIdentity(URI("https://$host/"), UUID.randomUUID(),
                        DeviceKeyManager(handle, listOf(leaf.certificate)))
                    val client = DeviceMqttTransport.client(identity, root.certificate, server.localPort)
                    val connection = client.connectWith().cleanSession(true).send()
                    if (host == "localhost") {
                        val result = observed.get(15, TimeUnit.SECONDS)
                        assertEquals(leaf.certificate, result.first)
                        assertEquals(0x10, result.second)
                    }
                    assertThrows(ExecutionException::class.java) { connection.get(15, TimeUnit.SECONDS) }
                    if (host != "localhost") {
                        try {
                            assertEquals(-1, observed.get(15, TimeUnit.SECONDS).second)
                        } catch (_: ExecutionException) {
                        }
                    }
                } finally {
                    executor.shutdownNow()
                    assertTrue(executor.awaitTermination(5, TimeUnit.SECONDS))
                }
            }
        }
    }
}