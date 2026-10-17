package dev.fleet.agent

import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.tls.HandshakeCertificates
import okhttp3.tls.HeldCertificate
import org.junit.Assert.*
import org.junit.Test
import java.net.URI
import java.util.UUID
import java.util.concurrent.TimeUnit

class CommandEventTransportTest {
    @Test fun enrolledTlsUploadsExactEventAndValidatesOriginalAndDuplicateAcknowledgements() = withServer { server, root, identity, event ->
        val acknowledgement = """{"schemaVersion":1,"deviceId":"${identity.deviceId}","eventId":"${event.eventId}","receivedAt":"2026-10-17T12:00:00.123456789Z"}"""
        repeat(2) {
            server.enqueue(MockResponse().setHeader("Content-Type", "application/json; charset=utf-8").setBody(acknowledgement))
            val accepted = CommandEventTransport.upload(identity, root.certificate, event)
            assertEquals(UUID.fromString(event.eventId), accepted.eventId)
            assertEquals("2026-10-17T12:00:00.123456789Z", accepted.receivedAt)
            val request = server.takeRequest(5, TimeUnit.SECONDS)!!
            assertEquals("POST", request.method)
            assertEquals("/api/v1/device/command-events", request.path)
            assertEquals(event.wireJson, request.body.readUtf8())
            assertNull(request.getHeader("Authorization"))
            assertNotNull(request.handshake!!.peerCertificates.single())
        }
    }

    @Test fun rejectsUntrustedMismatchedOversizedEncodedRedirectAndErrorResponses() = withServer { server, root, identity, event ->
        val acknowledgement = """{"schemaVersion":1,"deviceId":"${identity.deviceId}","eventId":"${event.eventId}","receivedAt":"2026-10-17T12:00:00Z"}"""
        val wrongHost = DeviceTlsIdentity(URI(identity.endpoint.toString().replace("localhost", "127.0.0.1")), identity.deviceId, identity.keyManager)
        assertThrows(javax.net.ssl.SSLException::class.java) { CommandEventTransport.upload(wrongHost, root.certificate, event) }
        val wrongRoot = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Other root").build()
        assertThrows(javax.net.ssl.SSLException::class.java) { CommandEventTransport.upload(identity, wrongRoot.certificate, event) }
        val bodies = listOf(acknowledgement.replace(event.eventId, UUID.randomUUID().toString()),
            acknowledgement.replace(identity.deviceId.toString(), UUID.randomUUID().toString()), "{}", " ".repeat(4097))
        for (body in bodies) {
            server.enqueue(MockResponse().setHeader("Content-Type", "application/json").setChunkedBody(body, 127))
            assertThrows(Exception::class.java) { CommandEventTransport.upload(identity, root.certificate, event) }
        }
        for (contentType in listOf("text/plain", "application/json; charset=utf-16")) {
            server.enqueue(MockResponse().setHeader("Content-Type", contentType).setBody(acknowledgement))
            assertThrows(Exception::class.java) { CommandEventTransport.upload(identity, root.certificate, event) }
        }
        server.enqueue(MockResponse().setHeader("Content-Type", "application/json").setHeader("Content-Encoding", "gzip").setBody(acknowledgement))
        assertThrows(Exception::class.java) { CommandEventTransport.upload(identity, root.certificate, event) }
        for (status in listOf(302, 401, 409, 429, 503)) {
            server.enqueue(MockResponse().setResponseCode(status).setHeader("Location", server.url("/unexpected")))
            assertEquals(status, assertThrows(DeviceHttpException::class.java) {
                CommandEventTransport.upload(identity, root.certificate, event)
            }.status)
        }
    }

    @Test fun mismatchedLocalRowsAndUnsafeOriginsAreRejectedBeforeSending() = withServer { server, root, identity, event ->
        for (row in listOf(event.copy(deviceId = UUID.randomUUID().toString()),
            event.copy(eventId = UUID.randomUUID().toString()), event.copy(commandId = UUID.randomUUID().toString()),
            event.copy(wireJson = " ".repeat(8193)))) {
            assertThrows(Exception::class.java) { CommandEventTransport.upload(identity, root.certificate, row) }
        }
        for (endpoint in listOf("http://localhost/api/v1/device/identity", "https://user@localhost/api/v1/device/identity",
            "https://localhost/api/v1/device/identity?query=1", "https://localhost/other")) {
            assertThrows(Exception::class.java) {
                CommandEventTransport.upload(DeviceTlsIdentity(URI(endpoint), identity.deviceId, identity.keyManager), root.certificate, event)
            }
        }
        assertEquals(0, server.requestCount)
    }

    private fun withServer(action: (MockWebServer, HeldCertificate, DeviceTlsIdentity, CommandEventRow) -> Unit) {
        val root = HeldCertificate.Builder().ecdsa256().certificateAuthority(1).commonName("Event root").build()
        val serverCertificate = HeldCertificate.Builder().ecdsa256().commonName("localhost").addSubjectAlternativeName("localhost").signedBy(root).build()
        val leaf = HeldCertificate.Builder().ecdsa256().commonName("Enrolled device").signedBy(root).build()
        val tls = HandshakeCertificates.Builder().heldCertificate(serverCertificate).addTrustedCertificate(root.certificate).build()
        MockWebServer().use { server ->
            server.useHttps(tls.sslSocketFactory(), false)
            server.requireClientAuth()
            server.start()
            val deviceId = UUID.randomUUID()
            val eventId = UUID.randomUUID()
            val commandId = UUID.randomUUID()
            val identity = DeviceTlsIdentity(server.url("/api/v1/device/identity").toUri(), deviceId,
                DeviceKeyManager(leaf.keyPair.private, listOf(leaf.certificate)))
            val event = CommandEventRow(eventId.toString(), commandId.toString(), deviceId.toString(),
                """{"schemaVersion":1,"deviceId":"$deviceId","eventId":"$eventId","commandId":"$commandId","correlationId":"${UUID.randomUUID()}","state":"received","recordedAt":"2026-10-17T12:00:00Z"}""")
            action(server, root, identity, event)
        }
    }
}