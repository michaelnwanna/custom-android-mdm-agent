package dev.fleet.agent

import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.tls.HandshakeCertificates
import okhttp3.tls.HeldCertificate
import org.junit.Assert.*
import org.junit.Test
import java.net.URI
import java.util.UUID

class EnrollmentTransportTest {
    @Test fun pinnedTlsSendsBoundedRequestAndRefusesRedirectsAndUntrustedServers() {
        val root = HeldCertificate.Builder().certificateAuthority(1).commonName("Enrollment TLS test root").build()
        val leaf = HeldCertificate.Builder().commonName("localhost").addSubjectAlternativeName("localhost").signedBy(root).build()
        val tls = HandshakeCertificates.Builder().heldCertificate(leaf, root.certificate).build()
        MockWebServer().use { server ->
            server.useHttps(tls.sslSocketFactory(), false)
            server.start()
            val token = "A".repeat(43)
            val bootstrap = EnrollmentBootstrap(server.url("/api/v1/enrollments").toUri(), UUID.randomUUID(), token)
            server.enqueue(MockResponse().setResponseCode(201).addHeader("Content-Type", "application/json").setBody("{}"))
            assertEquals("{}", EnrollmentTransport.redeem(bootstrap, "A".repeat(64), root.certificate).toString(Charsets.UTF_8))
            val request = server.takeRequest()
            assertEquals("Bearer $token", request.getHeader("Authorization"))
            assertEquals("/api/v1/enrollments", request.path)
            assertTrue(request.body.readUtf8().contains(bootstrap.pendingId.toString()))
            server.enqueue(MockResponse().setResponseCode(302).addHeader("Location", "https://untrusted.test/"))
            val redirect = assertThrows(EnrollmentHttpException::class.java) {
                EnrollmentTransport.redeem(bootstrap, "A".repeat(64), root.certificate)
            }
            assertEquals(302, redirect.status)
            server.takeRequest()
            val otherRoot = HeldCertificate.Builder().certificateAuthority(1).build()
            assertThrows(Exception::class.java) { EnrollmentTransport.redeem(bootstrap, "A".repeat(64), otherRoot.certificate) }
            val wrongHost = EnrollmentBootstrap(URI(bootstrap.endpoint.toString().replace("localhost", "127.0.0.1")), bootstrap.pendingId, token)
            assertThrows(Exception::class.java) { EnrollmentTransport.redeem(wrongHost, "A".repeat(64), root.certificate) }
            assertEquals(2, server.requestCount)
        }
    }

    @Test fun responseLimitAppliesToDeclaredAndChunkedBodies() {
        val root = HeldCertificate.Builder().certificateAuthority(1).build()
        val leaf = HeldCertificate.Builder().addSubjectAlternativeName("localhost").signedBy(root).build()
        MockWebServer().use { server ->
            server.useHttps(HandshakeCertificates.Builder().heldCertificate(leaf, root.certificate).build().sslSocketFactory(), false)
            server.start()
            val bootstrap = EnrollmentBootstrap(server.url("/api/v1/enrollments").toUri(), UUID.randomUUID(), "A".repeat(43))
            for (chunked in listOf(false, true)) {
                val response = MockResponse().setResponseCode(200).addHeader("Content-Type", "application/json")
                if (chunked) response.setChunkedBody("A".repeat(100001), 4096) else response.setBody("A".repeat(100001))
                server.enqueue(response)
                assertThrows(Exception::class.java) { EnrollmentTransport.redeem(bootstrap, "A".repeat(64), root.certificate) }
            }
        }
    }
}