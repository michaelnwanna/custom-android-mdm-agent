package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import java.time.Instant
import java.util.UUID

object EnrollmentResponse {
    const val MAXIMUM_BYTES = 100000

    fun parse(bytes: ByteArray): EnrollmentCertificateBundle {
        require(bytes.size <= MAXIMUM_BYTES) { "Enrollment response too large" }
        JsonReader.of(Buffer().write(bytes)).use { reader ->
            reader.beginObject()
            val fields = mutableSetOf<String>()
            var version: Int? = null
            var deviceId: UUID? = null
            var certificate: String? = null
            var intermediates: List<String>? = null
            var notBefore: Instant? = null
            var notAfter: Instant? = null
            while (reader.hasNext()) {
                val name = reader.nextName()
                require(fields.add(name)) { "Duplicate enrollment response field" }
                when (name) {
                    "schemaVersion" -> {
                        require(reader.peek() == JsonReader.Token.NUMBER)
                        version = reader.nextInt()
                    }
                    "deviceId" -> {
                        val text = string(reader)
                        deviceId = UUID.fromString(text)
                        require(deviceId.toString().equals(text, ignoreCase = true))
                    }
                    "certificateDer" -> certificate = string(reader)
                    "intermediateCertificatesDer" -> {
                        val chain = mutableListOf<String>()
                        reader.beginArray()
                        while (reader.hasNext()) {
                            require(chain.size < 4)
                            chain.add(string(reader))
                        }
                        reader.endArray()
                        intermediates = chain
                    }
                    "issuedAt" -> notBefore = Instant.parse(string(reader))
                    "expiresAt" -> notAfter = Instant.parse(string(reader))
                    else -> throw IllegalArgumentException("Unknown enrollment response field")
                }
            }
            reader.endObject()
            require(reader.peek() == JsonReader.Token.END_DOCUMENT)
            require(version == 1 && fields.size == 6)
            return EnrollmentCertificateBundle(requireNotNull(deviceId), requireNotNull(certificate),
                requireNotNull(intermediates), requireNotNull(notBefore), requireNotNull(notAfter))
        }
    }

    private fun string(reader: JsonReader): String {
        require(reader.peek() == JsonReader.Token.STRING)
        return reader.nextString()
    }
}