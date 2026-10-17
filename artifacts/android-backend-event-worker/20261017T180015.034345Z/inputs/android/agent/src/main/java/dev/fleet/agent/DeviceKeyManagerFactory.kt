package dev.fleet.agent

import java.security.KeyStore
import java.security.Provider
import javax.net.ssl.KeyManager
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.KeyManagerFactorySpi
import javax.net.ssl.ManagerFactoryParameters
import javax.net.ssl.X509ExtendedKeyManager

internal class DeviceKeyManagerFactory(manager: X509ExtendedKeyManager) : KeyManagerFactory(
    DeviceKeyManagerFactorySpi(manager), DeviceKeyManagerProvider(), "EnrolledDevice"
)

private class DeviceKeyManagerProvider : Provider("EnrolledDevice", 1.0, "Enrolled device key handle adapter")

private class DeviceKeyManagerFactorySpi(private val manager: X509ExtendedKeyManager) : KeyManagerFactorySpi() {
    override fun engineGetKeyManagers(): Array<KeyManager> = arrayOf(manager)
    override fun engineInit(store: KeyStore?, password: CharArray?) {
        throw UnsupportedOperationException("Enrolled key factory cannot be reinitialized")
    }
    override fun engineInit(parameters: ManagerFactoryParameters?) {
        throw UnsupportedOperationException("Enrolled key factory cannot be reinitialized")
    }
}