package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import java.net.URI
import java.util.Base64
import java.util.UUID

class EnrollmentBootstrap(val endpoint: URI, val pendingId: UUID, val token: String) {
    init {
        require(endpoint.scheme == "https" && endpoint.host != null && endpoint.rawUserInfo == null &&
            endpoint.rawQuery == null && endpoint.rawFragment == null &&
            endpoint.rawPath == "/api/v1/enrollments" && (endpoint.port == -1 || endpoint.port in 1..65535))
        require(pendingId != UUID(0, 0))
        require(token.length == 43 && Base64.getUrlEncoder().withoutPadding()
            .encodeToString(Base64.getUrlDecoder().decode(token)) == token)
    }

    override fun toString() = "EnrollmentBootstrap { [redacted] }"

    companion object {
        fun parse(bytes: ByteArray): EnrollmentBootstrap {
            require(bytes.size <= 4096)
            JsonReader.of(Buffer().write(bytes)).use { reader ->
                reader.beginObject()
                val seen = mutableSetOf<String>()
                var endpoint: URI? = null
                var pending: UUID? = null
                var token: String? = null
                var version: Int? = null
                while (reader.hasNext()) {
                    val name = reader.nextName()
                    require(seen.add(name))
                    if (name == "schemaVersion") {
                        require(reader.peek() == JsonReader.Token.NUMBER)
                        version = reader.nextInt()
                        continue
                    }
                    require(reader.peek() == JsonReader.Token.STRING)
                    val value = reader.nextString()
                    when (name) {
                        "endpoint" -> endpoint = URI(value)
                        "pendingEnrollmentId" -> {
                            pending = UUID.fromString(value)
                            require(pending.toString().equals(value, ignoreCase = true))
                        }
                        "bootstrapToken" -> token = value
                        else -> throw IllegalArgumentException("Unknown bootstrap field")
                    }
                }
                reader.endObject()
                require(reader.peek() == JsonReader.Token.END_DOCUMENT && seen.size == 4 && version == 1)
                return EnrollmentBootstrap(requireNotNull(endpoint), requireNotNull(pending), requireNotNull(token))
            }
        }
    }
}