package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.security.KeyStore
import java.security.cert.X509Certificate
import java.util.UUID
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory

class DeviceHttpException(val status: Int) : IOException("Device server rejected request")

object DeviceIdentityTransport {
    fun check(identity: DeviceTlsIdentity, serverRoot: X509Certificate): UUID {
        val endpoint = identity.endpoint
        require(endpoint.scheme == "https" && !endpoint.host.isNullOrEmpty() && endpoint.userInfo == null &&
            endpoint.query == null && endpoint.fragment == null && endpoint.path == "/api/v1/device/identity")
        val store = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null)
            setCertificateEntry("server-root", serverRoot)
        }
        val trust = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(store) }
        val tls = SSLContext.getInstance("TLS").apply { init(arrayOf(identity.keyManager), trust.trustManagers, null) }
        val connection = endpoint.toURL().openConnection() as HttpsURLConnection
        try {
            connection.sslSocketFactory = tls.socketFactory
            connection.instanceFollowRedirects = false
            connection.useCaches = false
            connection.connectTimeout = 10000
            connection.readTimeout = 20000
            connection.requestMethod = "GET"
            connection.setRequestProperty("Accept", "application/json")
            if (connection.responseCode != 200) throw DeviceHttpException(connection.responseCode)
            require(connection.contentType?.substringBefore(';')?.trim()?.equals("application/json", true) == true)
            require(connection.contentLengthLong <= 4096)
            val output = ByteArrayOutputStream()
            connection.inputStream.use { stream ->
                val chunk = ByteArray(1024)
                while (true) {
                    val count = stream.read(chunk, 0, minOf(chunk.size, 4097 - output.size()))
                    if (count < 0) break
                    output.write(chunk, 0, count)
                    require(output.size() <= 4096)
                }
            }
            JsonReader.of(Buffer().write(output.toByteArray())).use { reader ->
                val seen = mutableSetOf<String>()
                reader.beginObject()
                while (reader.hasNext()) {
                    val name = reader.nextName()
                    require(seen.add(name))
                    when (name) {
                        "schemaVersion" -> {
                            require(reader.peek() == JsonReader.Token.NUMBER)
                            require(reader.nextString() == "1")
                        }
                        "deviceId" -> {
                            require(reader.peek() == JsonReader.Token.STRING)
                            require(reader.nextString() == identity.deviceId.toString())
                        }
                        else -> throw IllegalArgumentException("Unknown device identity field")
                    }
                }
                reader.endObject()
                require(seen == setOf("schemaVersion", "deviceId") && reader.peek() == JsonReader.Token.END_DOCUMENT)
            }
            return identity.deviceId
        } finally {
            connection.disconnect()
        }
    }
}