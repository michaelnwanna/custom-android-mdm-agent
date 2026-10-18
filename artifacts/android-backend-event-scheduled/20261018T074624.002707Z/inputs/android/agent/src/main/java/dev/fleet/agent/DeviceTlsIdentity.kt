package dev.fleet.agent

import java.net.Socket
import java.net.URI
import java.security.Principal
import java.security.PrivateKey
import java.security.cert.X509Certificate
import java.util.UUID
import javax.net.ssl.SSLEngine
import javax.net.ssl.X509ExtendedKeyManager

class DeviceTlsIdentity internal constructor(
    val endpoint: URI,
    val deviceId: UUID,
    internal val keyManager: X509ExtendedKeyManager
)

internal class DeviceKeyManager(private val privateKey: PrivateKey, chain: List<X509Certificate>) : X509ExtendedKeyManager() {
    private val certificates = chain.toTypedArray()
    private val alias = "enrolled-device"

    private fun eligible(keyType: String?, issuers: Array<out Principal>?): Boolean = keyType == "EC" &&
        (issuers.isNullOrEmpty() || certificates.any { issuers.contains(it.issuerX500Principal) })

    override fun getClientAliases(keyType: String?, issuers: Array<out Principal>?): Array<String>? =
        if (eligible(keyType, issuers)) arrayOf(alias) else null

    override fun chooseClientAlias(keyTypes: Array<out String>?, issuers: Array<out Principal>?, socket: Socket?): String? =
        if (keyTypes?.any { eligible(it, issuers) } == true) alias else null

    override fun chooseEngineClientAlias(keyTypes: Array<out String>?, issuers: Array<out Principal>?, engine: SSLEngine?): String? =
        chooseClientAlias(keyTypes, issuers, null)

    override fun getCertificateChain(selected: String?): Array<X509Certificate>? =
        if (selected == alias) certificates.copyOf() else null

    override fun getPrivateKey(selected: String?): PrivateKey? = if (selected == alias) privateKey else null
    override fun getServerAliases(keyType: String?, issuers: Array<out Principal>?): Array<String>? = null
    override fun chooseServerAlias(keyType: String?, issuers: Array<out Principal>?, socket: Socket?): String? = null
}