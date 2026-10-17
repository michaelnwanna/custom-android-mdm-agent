package dev.fleet.agent

import org.junit.Assert.*
import org.junit.Test
import java.net.URI
import java.util.UUID

class EnrollmentBootstrapTest {
    private val token = "A".repeat(43)
    private val pending = UUID.randomUUID()

    @Test fun allowsOnlyExplicitHttpsBootstrapWithoutRedirectTargetsOrSecretUrls() {
        val bootstrap = EnrollmentBootstrap(URI("https://fleet.example.test/api/v1/enrollments"), pending, token)
        assertFalse(bootstrap.toString().contains(token))
        for (address in listOf("http://fleet.test/api/v1/enrollments", "https://user@fleet.test/api/v1/enrollments",
            "https://fleet.test/api/v1/enrollments?token=secret", "https://fleet.test/api/v1/enrollments#fragment",
            "https://fleet.test/wrong", "https://fleet.test:0/api/v1/enrollments"))
            assertThrows(Exception::class.java) { EnrollmentBootstrap(URI(address), pending, token) }
        assertThrows(Exception::class.java) { EnrollmentBootstrap(bootstrap.endpoint, UUID(0, 0), token) }
        assertThrows(Exception::class.java) { EnrollmentBootstrap(bootstrap.endpoint, pending, "B".repeat(43)) }
    }

    @Test fun stagedConfigurationRequiresExactVersionedFieldsAndCanonicalToken() {
        val body = """{"schemaVersion":1,"endpoint":"https://fleet.test/api/v1/enrollments","pendingEnrollmentId":"$pending","bootstrapToken":"$token"}"""
        assertEquals(pending, EnrollmentBootstrap.parse(body.toByteArray()).pendingId)
        for (text in listOf(body.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            body.replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            body.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"unexpected\":\"value\""), body + "{}"))
            assertThrows(Exception::class.java) { EnrollmentBootstrap.parse(text.toByteArray()) }
    }
}