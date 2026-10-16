package dev.fleet.agent

import org.junit.Assert.*
import org.junit.Test

class EnrollmentResponseTest {
    private val body = """{"schemaVersion":1,"deviceId":"a9373871-de89-4723-bc55-884e76916e67","certificateDer":"shape-only","intermediateCertificatesDer":[],"notBefore":"2026-10-17T12:00:00Z","notAfter":"2026-10-18T12:00:00Z"}"""

    @Test fun parsesBoundedVersionedResponseWithoutGrantingCertificateTrust() {
        val parsed = EnrollmentResponse.parse(body.toByteArray())
        assertEquals("a9373871-de89-4723-bc55-884e76916e67", parsed.deviceId.toString())
        assertEquals("shape-only", parsed.certificateDer)
        assertTrue(parsed.intermediateCertificatesDer.isEmpty())
    }

    @Test fun rejectsDuplicateUnknownVersionTypeAndMissingFields() {
        val invalid = listOf(
            body.replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            body.replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""),
            body.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            body.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"\\u0073chemaVersion\":1"),
            body.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"rootCertificate\":\"untrusted\""),
            body.replace("\"certificateDer\":\"shape-only\",", ""),
            body.replace("\"certificateDer\":\"shape-only\"", "\"certificateDer\":1"),
            body.replace("a9373871-de89-4723-bc55-884e76916e67", "1-2-3-4-5"),
            body.replace("2026-10-17T12:00:00Z", "today"),
            body + "{}", "[]", "null"
        )
        for (text in invalid)
            assertThrows(Exception::class.java) { EnrollmentResponse.parse(text.toByteArray()) }
    }

    @Test fun rejectsOversizedBodiesAndChains() {
        assertThrows(Exception::class.java) { EnrollmentResponse.parse(ByteArray(100001)) }
        assertThrows(Exception::class.java) {
            EnrollmentResponse.parse(body.replace("[]", "[\"a\",\"b\",\"c\",\"d\",\"e\"]").toByteArray())
        }
    }
}