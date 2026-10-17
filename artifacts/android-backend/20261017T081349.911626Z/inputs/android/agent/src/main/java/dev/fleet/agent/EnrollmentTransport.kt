package dev.fleet.agent

import com.squareup.moshi.JsonWriter
import okio.Buffer
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.security.KeyStore
import java.security.cert.X509Certificate
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory

class EnrollmentHttpException(val status: Int) : IOException("Enrollment server rejected request")

object EnrollmentTransport {
    fun redeem(bootstrap: EnrollmentBootstrap, csr: String, serverRoot: X509Certificate): ByteArray {
        require(csr.length in 64..16384)
        val buffer = Buffer()
        JsonWriter.of(buffer).use { writer ->
            writer.beginObject().name("schemaVersion").value(1L)
                .name("pendingEnrollmentId").value(bootstrap.pendingId.toString())
                .name("csrDer").value(csr).endObject()
        }
        val body = buffer.readByteArray()
        require(body.size <= 24576)
        val store = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null)
            setCertificateEntry("server-root", serverRoot)
        }
        val managers = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(store) }
        val tls = SSLContext.getInstance("TLS").apply { init(null, managers.trustManagers, null) }
        val connection = bootstrap.endpoint.toURL().openConnection() as HttpsURLConnection
        try {
            connection.sslSocketFactory = tls.socketFactory
            connection.instanceFollowRedirects = false
            connection.useCaches = false
            connection.connectTimeout = 10000
            connection.readTimeout = 20000
            connection.requestMethod = "POST"
            connection.doOutput = true
            connection.setRequestProperty("Authorization", "Bearer ${bootstrap.token}")
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            connection.setRequestProperty("Accept", "application/json")
            connection.setFixedLengthStreamingMode(body.size)
            connection.outputStream.use { it.write(body) }
            val status = connection.responseCode
            if (status != 200 && status != 201) throw EnrollmentHttpException(status)
            require(connection.contentType?.substringBefore(';')?.trim()?.equals("application/json", true) == true)
            require(connection.contentLengthLong <= EnrollmentResponse.MAXIMUM_BYTES)
            val output = ByteArrayOutputStream()
            connection.inputStream.use { stream ->
                val chunk = ByteArray(4096)
                while (true) {
                    val count = stream.read(chunk, 0, minOf(chunk.size, EnrollmentResponse.MAXIMUM_BYTES + 1 - output.size()))
                    if (count < 0) break
                    output.write(chunk, 0, count)
                    require(output.size() <= EnrollmentResponse.MAXIMUM_BYTES) { "Enrollment response too large" }
                }
            }
            return output.toByteArray()
        } finally {
            connection.disconnect()
        }
    }
}