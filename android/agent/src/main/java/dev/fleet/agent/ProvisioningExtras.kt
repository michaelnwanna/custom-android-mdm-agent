package dev.fleet.agent

import java.io.ByteArrayInputStream
import java.io.File
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.util.Base64

/**
 * Android Enterprise QR admin extras (PROVISIONING_ADMIN_EXTRAS_BUNDLE) carrying the enrollment
 * bootstrap and the two operator-supplied trust roots, staged into the same private files used by
 * [LabEnrollment].
 */
class ProvisioningExtras private constructor(
    private val bootstrap: ByteArray,
    private val serverRoot: X509Certificate,
    private val deviceRoot: X509Certificate,
) {
    override fun toString() = "ProvisioningExtras { [redacted] }"

    /**
     * Writes roots and bootstrap. Existing roots must be identical (trust is never replaced) and an
     * existing staged bootstrap is never overwritten.
     */
    fun stage(directory: File) {
        val serverFile = File(directory, SERVER_ROOT_FILE)
        val deviceFile = File(directory, DEVICE_ROOT_FILE)
        val bootstrapFile = File(directory, BOOTSTRAP_FILE)
        val serverPem = pem(serverRoot)
        val devicePem = pem(deviceRoot)
        for ((file, bytes) in listOf(serverFile to serverPem, deviceFile to devicePem))
            require(!file.exists() || file.readBytes().contentEquals(bytes)) { "Refusing to replace an existing trust root" }
        require(!bootstrapFile.exists()) { "A staged bootstrap already exists" }
        if (!serverFile.exists()) write(serverFile, serverPem)
        if (!deviceFile.exists()) write(deviceFile, devicePem)
        write(bootstrapFile, bootstrap)
    }

    companion object {
        const val BOOTSTRAP_KEY = "dev.fleet.agent.ENROLLMENT_BOOTSTRAP"
        const val SERVER_ROOT_KEY = "dev.fleet.agent.SERVER_ROOT_CA"
        const val DEVICE_ROOT_KEY = "dev.fleet.agent.DEVICE_ROOT_CA"
        const val SERVER_ROOT_FILE = "enrollment-server-ca.pem"
        const val DEVICE_ROOT_FILE = "enrollment-device-ca.pem"
        const val BOOTSTRAP_FILE = "enrollment-bootstrap.json"
        private val keys = setOf(BOOTSTRAP_KEY, SERVER_ROOT_KEY, DEVICE_ROOT_KEY)

        fun parse(extras: Map<String, Any?>): ProvisioningExtras {
            require(extras.keys == keys) { "Unexpected provisioning extras" }
            val bootstrap = (extras[BOOTSTRAP_KEY] as? String)?.toByteArray(Charsets.UTF_8)
            requireNotNull(bootstrap)
            EnrollmentBootstrap.parse(bootstrap)
            return ProvisioningExtras(bootstrap, root(extras[SERVER_ROOT_KEY]), root(extras[DEVICE_ROOT_KEY]))
        }

        private fun root(value: Any?): X509Certificate {
            val text = requireNotNull(value as? String)
            require(text.length in 1..8192)
            val der = Base64.getDecoder().decode(text)
            require(Base64.getEncoder().encodeToString(der) == text)
            val certificates = CertificateFactory.getInstance("X.509").generateCertificates(ByteArrayInputStream(der))
            require(certificates.size == 1)
            val certificate = certificates.single() as X509Certificate
            require(certificate.encoded.contentEquals(der) && certificate.basicConstraints >= 0) { "Root must be a CA" }
            return certificate
        }

        private fun pem(certificate: X509Certificate): ByteArray =
            ("-----BEGIN CERTIFICATE-----\n" +
                Base64.getMimeEncoder(64, "\n".toByteArray()).encodeToString(certificate.encoded) +
                "\n-----END CERTIFICATE-----\n").toByteArray(Charsets.US_ASCII)

        private fun write(file: File, bytes: ByteArray) {
            val temporary = File(file.parentFile, ".${file.name}.tmp")
            temporary.delete()
            temporary.outputStream().use { stream ->
                stream.write(bytes)
                stream.fd.sync()
            }
            require(temporary.renameTo(file)) { "Could not stage ${file.name}" }
        }
    }
}
