package dev.fleet.agent

import okhttp3.tls.HeldCertificate
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.security.cert.CertificateFactory
import java.util.Base64
import java.util.UUID

class ProvisioningExtrasTest {
    @get:Rule val folder = TemporaryFolder()

    private val token = "A".repeat(43)
    private val bootstrap =
        """{"schemaVersion":1,"endpoint":"https://fleet.test/api/v1/enrollments","pendingEnrollmentId":"${UUID.randomUUID()}","bootstrapToken":"$token"}"""
    private val serverRoot = HeldCertificate.Builder().certificateAuthority(0).ecdsa256().build().certificate
    private val deviceRoot = HeldCertificate.Builder().certificateAuthority(0).ecdsa256().build().certificate
    private fun der(certificate: java.security.cert.X509Certificate) = Base64.getEncoder().encodeToString(certificate.encoded)
    private fun extras(
        bootstrapValue: Any? = bootstrap,
        server: Any? = der(serverRoot),
        device: Any? = der(deviceRoot),
    ) = mapOf(
        ProvisioningExtras.BOOTSTRAP_KEY to bootstrapValue,
        ProvisioningExtras.SERVER_ROOT_KEY to server,
        ProvisioningExtras.DEVICE_ROOT_KEY to device,
    )

    @Test fun stagesBootstrapAndRootsInLabEnrollmentFormat() {
        val directory = folder.newFolder()
        val parsed = ProvisioningExtras.parse(extras())
        assertFalse(parsed.toString().contains(token))
        parsed.stage(directory)

        assertEquals(bootstrap, File(directory, ProvisioningExtras.BOOTSTRAP_FILE).readText())
        for ((name, expected) in listOf(ProvisioningExtras.SERVER_ROOT_FILE to serverRoot, ProvisioningExtras.DEVICE_ROOT_FILE to deviceRoot)) {
            val certificates = File(directory, name).inputStream().use { CertificateFactory.getInstance("X.509").generateCertificates(it) }
            assertEquals(listOf(expected), certificates.toList())
        }
        assertEquals(3, directory.list()!!.size)
    }

    @Test fun rejectsMissingUnknownOrMalformedExtras() {
        val leaf = HeldCertificate.Builder().ecdsa256().build().certificate
        for (invalid in listOf(
            extras() - ProvisioningExtras.DEVICE_ROOT_KEY,
            extras() + ("unexpected" to "value"),
            extras(bootstrapValue = bootstrap.replace("\"schemaVersion\":1", "\"schemaVersion\":2")),
            extras(bootstrapValue = 1),
            extras(server = "not base64!"),
            extras(server = der(serverRoot) + "\n"),
            extras(server = der(leaf)),
            extras(device = null),
        )) assertThrows(Exception::class.java) { ProvisioningExtras.parse(invalid) }
    }

    @Test fun neverReplacesTrustRootsOrAStagedBootstrap() {
        val directory = folder.newFolder()
        ProvisioningExtras.parse(extras()).stage(directory)
        val staged = File(directory, ProvisioningExtras.BOOTSTRAP_FILE).readBytes()

        assertThrows(Exception::class.java) { ProvisioningExtras.parse(extras()).stage(directory) }
        assertArrayEquals(staged, File(directory, ProvisioningExtras.BOOTSTRAP_FILE).readBytes())

        File(directory, ProvisioningExtras.BOOTSTRAP_FILE).delete()
        val otherRoot = HeldCertificate.Builder().certificateAuthority(0).ecdsa256().build().certificate
        assertThrows(Exception::class.java) { ProvisioningExtras.parse(extras(server = der(otherRoot))).stage(directory) }
        assertFalse(File(directory, ProvisioningExtras.BOOTSTRAP_FILE).exists())
        val kept = File(directory, ProvisioningExtras.SERVER_ROOT_FILE).inputStream().use {
            CertificateFactory.getInstance("X.509").generateCertificate(it)
        }
        assertEquals(serverRoot, kept)

        ProvisioningExtras.parse(extras()).stage(directory)
        assertTrue(File(directory, ProvisioningExtras.BOOTSTRAP_FILE).exists())
    }
}
