package dev.fleet.agent

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.IBinder
import com.hivemq.client.mqtt.MqttClient
import com.hivemq.client.mqtt.datatypes.MqttQos
import com.hivemq.client.mqtt.mqtt3.Mqtt3AsyncClient
import java.security.KeyStore
import java.security.cert.CertificateFactory
import javax.net.ssl.TrustManagerFactory

class LabMqttService : Service() {
    private var client: Mqtt3AsyncClient? = null
    private val preferences by lazy { getSharedPreferences("lab-mqtt", MODE_PRIVATE) }

    private fun record(message: String) {
        preferences.edit().putString("status", message).apply()
    }

    override fun onCreate() {
        super.onCreate()
        val notifications = getSystemService(NotificationManager::class.java)
        notifications.createNotificationChannel(NotificationChannel("lab-mqtt", "Lab MQTT diagnostics", NotificationManager.IMPORTANCE_LOW))
        val pending = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        val notification = Notification.Builder(this, "lab-mqtt")
            .setSmallIcon(android.R.drawable.stat_notify_sync)
            .setContentTitle("Fleet Lab MQTT diagnostics")
            .setContentText("Explicitly started TLS lifecycle probe")
            .setContentIntent(pending).setOngoing(true).build()
        startForeground(34, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        try {
            val certificate = filesDir.resolve("mqtt-ca.pem").inputStream().use {
                CertificateFactory.getInstance("X.509").generateCertificate(it)
            }
            val store = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
                load(null)
                setCertificateEntry("lab", certificate)
            }
            val trust = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(store) }
            val mqtt = MqttClient.builder().useMqttVersion3().identifier("fleet-lab-agent")
                .serverHost("localhost").serverPort(8883)
                .sslConfig().trustManagerFactory(trust).applySslConfig()
                .automaticReconnectWithDefaultConfig()
                .addDisconnectedListener { record("MQTT disconnected; bounded client backoff active.") }
                .addConnectedListener { record("MQTT connected over lab TLS.") }
                .buildAsync()
            client = mqtt
            mqtt.subscribeWith().topicFilter("fleet/lab/probe").qos(MqttQos.AT_LEAST_ONCE)
                .callback { publish ->
                    val message = String(publish.payloadAsBytes, Charsets.UTF_8)
                    if (message.matches(Regex("probe-[0-9a-f-]{36}"))) {
                        val count = preferences.getInt("received", 0) + 1
                        preferences.edit().putInt("received", count).putString("lastProbe", message)
                            .putString("status", "MQTT probe received: $count").commit()
                        mqtt.publishWith().topic("fleet/lab/ack").qos(MqttQos.AT_LEAST_ONCE)
                            .payload(message.toByteArray(Charsets.UTF_8)).send()
                    }
                }.send().whenComplete { _, error ->
                    if (error != null) record("MQTT subscribe failed: ${error.javaClass.simpleName}")
                }
            mqtt.connectWith().cleanSession(false).keepAlive(15).send().whenComplete { _, error ->
                if (error != null) record("MQTT connection failed: ${error.javaClass.simpleName}")
            }
        } catch (exception: Exception) {
            record("MQTT startup failed: ${exception.javaClass.simpleName}")
            stopSelf()
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int = START_NOT_STICKY

    override fun onDestroy() {
        client?.disconnect()
        record("MQTT probe stopped; no automatic service restart.")
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null
}