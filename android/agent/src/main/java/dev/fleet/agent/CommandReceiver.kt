package dev.fleet.agent

import android.app.Application
import android.content.Context
import com.hivemq.client.mqtt.datatypes.MqttQos
import com.hivemq.client.mqtt.mqtt3.Mqtt3AsyncClient
import java.security.cert.X509Certificate
import java.time.Clock
import java.util.UUID
import java.util.concurrent.Callable

/**
 * Manages MQTT subscription and command reception.
 * Receives commands from backend via MQTT and dispatches to handlers.
 */
internal class CommandReceiver(
    private val client: Mqtt3AsyncClient,
    private val journal: CommandJournal,
    private val policyHandler: PolicyHandler,
    private val deviceId: UUID,
    private val clock: Clock = Clock.systemUTC(),
) {
    /**
     * Subscribe to command topic and set up message handler.
     */
    fun subscribeToCommands(onCommandProcessed: (CommandReceipt, CommandPayload?) -> Unit) {
        val topic = "fleet/devices/$deviceId/commands"
        client.subscribeWith()
            .topicFilter(topic)
            .qos(MqttQos.AT_LEAST_ONCE)
            .callback { publish ->
                try {
                    val receipt = journal.receiveFromMqtt(topic, publish.payloadAsBytes, publish.isRetain)
                    
                    // Decode and process the command
                    val command = CommandDecoder.decode(publish.payloadAsBytes, deviceId)
                    var handlerResult: Any? = null
                    
                    when (command.payload) {
                        is CommandPayload.SetPolicy -> {
                            handlerResult = policyHandler.handleSetPolicyCommand(command)
                        }
                        is CommandPayload.InstallRelease -> {
                            // TODO: implement release installation handler
                        }
                        CommandPayload.RequestSync -> {
                            // Request device to sync state; handled by EventUploadScheduler
                        }
                    }
                    
                    onCommandProcessed(receipt, command.payload)
                } catch (error: Exception) {
                    // Errors in command processing should not crash the receiver
                    // Log the error for diagnostics
                }
            }
            .send()
    }

    /**
     * Unsubscribe from command topic.
     */
    fun unsubscribeFromCommands() {
        val topic = "fleet/devices/$deviceId/commands"
        client.unsubscribeWith()
            .topicFilter(topic)
            .send()
    }
}

/**
 * Manages MQTT client lifecycle and command reception integration.
 */
internal class MqttCommandService(
    private val database: CommandDatabase,
    private val policyDatabase: PolicyDatabase,
    private val identity: DeviceTlsIdentity,
    private val serverRoot: X509Certificate,
    private val port: Int = 8883,
) : java.io.Closeable {
    private var client: Mqtt3AsyncClient? = null
    private var receiver: CommandReceiver? = null

    /**
     * Connect to MQTT broker and start receiving commands.
     */
    fun connect(
        onCommandProcessed: (CommandReceipt, CommandPayload?) -> Unit = { _, _ -> },
    ) {
        val mqttClient = DeviceMqttTransport.client(identity, serverRoot, port)
        client = mqttClient

        mqttClient.connectWith()
            .cleanSession(false)
            .keepAlive(60)
            .send()
            .whenComplete { _, error ->
                if (error != null) {
                    // Connection failed; client will retry with backoff
                    return@whenComplete
                }

                // Connected; set up command receiver
                val journal = CommandJournal(database, identity.deviceId)
                val policyHandler = PolicyHandler(policyDatabase, identity.deviceId)
                val commandReceiver = CommandReceiver(mqttClient, journal, policyHandler, identity.deviceId)
                receiver = commandReceiver

                commandReceiver.subscribeToCommands(onCommandProcessed)
            }
    }

    /**
     * Disconnect from MQTT broker.
     */
    fun disconnect() {
        receiver?.unsubscribeFromCommands()
        client?.disconnect()
        client = null
        receiver = null
    }

    override fun close() {
        disconnect()
        database.close()
        policyDatabase.close()
    }
}
