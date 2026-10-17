package dev.fleet.agent

import com.squareup.moshi.JsonReader
import okio.Buffer
import java.io.ByteArrayOutputStream
import java.security.KeyStore
import java.security.cert.X509Certificate
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManagerFactory

internal object CommandEventTransport {
    private val deadlines = Executors.newSingleThreadScheduledExecutor { task ->
        Thread(task, "event-upload-deadlines").apply { isDaemon = true }
    }

    fun upload(identity: DeviceTlsIdentity, serverRoot: X509Certificate, event: CommandEventRow,
        control: EventUploadControl? = null): EventAcknowledgement {
        val source = identity.endpoint
        require(source.scheme == "https" && !source.host.isNullOrEmpty() && source.userInfo == null &&
            source.query == null && source.fragment == null && source.path == "/api/v1/device/identity")
        val eventId = UUID.fromString(event.eventId)
        require(event.eventId == eventId.toString() && event.deviceId == identity.deviceId.toString())
        val bytes = event.wireJson.toByteArray(Charsets.UTF_8)
        require(bytes.isNotEmpty() && bytes.size <= CommandDecoder.MAXIMUM_BYTES)
        JsonReader.of(Buffer().write(bytes)).use { reader ->
            val seen = mutableSetOf<String>()
            reader.beginObject()
            while (reader.hasNext()) {
                val name = reader.nextName()
                require(seen.add(name))
                val expected = when (name) {
                    "deviceId" -> event.deviceId
                    "eventId" -> event.eventId
                    "commandId" -> event.commandId
                    else -> null
                }
                if (expected != null) {
                    require(reader.peek() == JsonReader.Token.STRING && reader.nextString() == expected)
                } else reader.skipValue()
            }
            reader.endObject()
            require(seen.containsAll(setOf("deviceId", "eventId", "commandId")) && reader.peek() == JsonReader.Token.END_DOCUMENT)
        }
        val store = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null)
            setCertificateEntry("server-root", serverRoot)
        }
        val trust = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(store) }
        val tls = SSLContext.getInstance("TLS").apply { init(arrayOf(identity.keyManager), trust.trustManagers, null) }
        val connection = source.resolve("/api/v1/device/command-events").toURL().openConnection() as HttpsURLConnection
        var deadline: java.util.concurrent.ScheduledFuture<*>? = null
        try {
            control?.attach { connection.disconnect() }
            if (control != null) deadline = deadlines.schedule({ control.cancel() },
                EventDeliveryPolicy.REQUEST_DEADLINE_SECONDS, TimeUnit.SECONDS)
            connection.sslSocketFactory = tls.socketFactory
            connection.instanceFollowRedirects = false
            connection.useCaches = false
            connection.connectTimeout = 10000
            connection.readTimeout = 20000
            connection.requestMethod = "POST"
            connection.doOutput = true
            connection.setFixedLengthStreamingMode(bytes.size)
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            connection.setRequestProperty("Accept", "application/json")
            connection.outputStream.use { it.write(bytes) }
            if (connection.responseCode != 200) throw DeviceHttpException(connection.responseCode)
            require(connection.contentType?.matches(Regex("application/json(?:\\s*;\\s*charset\\s*=\\s*(?:utf-8|\"utf-8\"))?", RegexOption.IGNORE_CASE)) == true)
            require(connection.contentEncoding == null)
            require(connection.contentLengthLong <= EventAcknowledgementDecoder.MAXIMUM_BYTES)
            val output = ByteArrayOutputStream()
            connection.inputStream.use { stream ->
                val chunk = ByteArray(1024)
                while (true) {
                    val count = stream.read(chunk, 0, minOf(chunk.size, EventAcknowledgementDecoder.MAXIMUM_BYTES + 1 - output.size()))
                    if (count < 0) break
                    output.write(chunk, 0, count)
                    require(output.size() <= EventAcknowledgementDecoder.MAXIMUM_BYTES)
                }
            }
            return EventAcknowledgementDecoder.decode(output.toByteArray(), identity.deviceId, eventId)
        } finally {
            deadline?.cancel(false)
            control?.detach()
            connection.disconnect()
        }
    }
}