package dev.fleet.agent

import com.hivemq.client.mqtt.MqttClient
import com.hivemq.client.mqtt.mqtt3.Mqtt3AsyncClient
import java.security.KeyStore
import java.security.cert.X509Certificate
import javax.net.ssl.TrustManagerFactory

internal object DeviceMqttTransport {
    fun client(identity: DeviceTlsIdentity, serverRoot: X509Certificate, port: Int): Mqtt3AsyncClient {
        val endpoint = identity.endpoint
        require(endpoint.scheme == "https" && !endpoint.host.isNullOrEmpty() && endpoint.rawUserInfo == null &&
            endpoint.rawQuery == null && endpoint.rawFragment == null && port in 1..65535) {
            "Invalid enrolled MQTT authority"
        }
        val trustStore = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null)
            setCertificateEntry("server-root", serverRoot)
        }
        val trust = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(trustStore) }
        return MqttClient.builder().useMqttVersion3().identifier(identity.deviceId.toString())
            .serverHost(endpoint.host).serverPort(port)
            .sslConfig().trustManagerFactory(trust)
            .keyManagerFactory(DeviceKeyManagerFactory(identity.keyManager)).applySslConfig()
            .buildAsync()
    }
}