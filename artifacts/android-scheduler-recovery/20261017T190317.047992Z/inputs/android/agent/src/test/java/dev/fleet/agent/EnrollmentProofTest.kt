package dev.fleet.agent

import org.bouncycastle.asn1.x500.X500Name
import org.bouncycastle.asn1.x509.BasicConstraints
import org.bouncycastle.asn1.x509.ExtendedKeyUsage
import org.bouncycastle.asn1.x509.Extension
import org.bouncycastle.asn1.x509.GeneralName
import org.bouncycastle.asn1.x509.GeneralNames
import org.bouncycastle.asn1.x509.KeyPurposeId
import org.bouncycastle.asn1.x509.KeyUsage
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder
import org.bouncycastle.operator.jcajce.JcaContentVerifierProviderBuilder
import org.bouncycastle.pkcs.PKCS10CertificationRequest
import org.junit.Assert.*
import org.junit.Test
import java.math.BigInteger
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.cert.X509Certificate
import java.security.spec.ECGenParameterSpec
import java.time.Instant
import java.util.Base64
import java.util.Date
import java.util.UUID

class EnrollmentProofTest {
    private val now = Instant.parse("2026-10-17T12:00:00Z")

    @Test fun csrHasVerifiedP256ProofOfPossession() {
        val keys = key()
        val request = PKCS10CertificationRequest(Base64.getDecoder().decode(EnrollmentProof.createCsr(keys)))
        assertTrue(request.isSignatureValid(JcaContentVerifierProviderBuilder().build(keys.public)))
        assertEquals("1.2.840.10045.4.3.2", request.signatureAlgorithm.algorithm.id)
        assertArrayEquals(keys.public.encoded, request.subjectPublicKeyInfo.encoded)
        assertFalse(request.isSignatureValid(JcaContentVerifierProviderBuilder().build(key().public)))
    }

    @Test fun csrRejectsAnotherCurve() {
        val keys = KeyPairGenerator.getInstance("EC").apply { initialize(ECGenParameterSpec("secp384r1")) }.generateKeyPair()
        assertThrows(IllegalArgumentException::class.java) { EnrollmentProof.createCsr(keys) }
    }

    @Test fun validRootSignedClientCertificateMatchesKeyIdentityAndTimes() {
        val fixture = fixture()
        val certificates = EnrollmentProof.verify(fixture.response, fixture.keys.public, fixture.root, now)
        assertEquals(1, certificates.size)
    }

    @Test fun rejectsWrongKeyIdentityTrustExpiryAndMetadata() {
        val fixture = fixture()
        fun reject(response: EnrollmentCertificateBundle) {
            assertThrows(Exception::class.java) { EnrollmentProof.verify(response, fixture.keys.public, fixture.root, now) }
        }
        reject(fixture.response.copy(deviceId = UUID.randomUUID()))
        reject(fixture.response.copy(notAfter = now))
        reject(fixture.response.copy(notBefore = now))
        reject(fixture.response.copy(certificateDer = fixture.response.certificateDer + "\n"))
        reject(fixture.response.copy(intermediateCertificatesDer = listOf(fixture.response.certificateDer)))
        reject(fixture.response.copy(intermediateCertificatesDer = listOf(encode(fixture.root))))
        assertThrows(Exception::class.java) { EnrollmentProof.verify(fixture.response, key().public, fixture.root, now) }
        assertThrows(Exception::class.java) { EnrollmentProof.verify(fixture.response, fixture.keys.public, fixture().root, now) }
        assertThrows(Exception::class.java) { EnrollmentProof.verify(fixture.response, fixture.keys.public, fixture.root, fixture.response.notAfter) }
    }

    @Test fun rejectsExtraIdentityServerUsageAndCaLeaf() {
        for (kind in listOf("extra-san", "server", "ca")) {
            val fixture = fixture(kind)
            assertThrows(Exception::class.java) { EnrollmentProof.verify(fixture.response, fixture.keys.public, fixture.root, now) }
        }
    }

    private data class Fixture(val keys: KeyPair, val root: X509Certificate, val response: EnrollmentCertificateBundle)

    private fun fixture(kind: String = "valid"): Fixture {
        val rootKey = key()
        val rootName = X500Name("CN=Enrollment test root")
        val rootBuilder = JcaX509v3CertificateBuilder(rootName, BigInteger.ONE,
            Date.from(now.minusSeconds(3600)), Date.from(now.plusSeconds(604800)), rootName, rootKey.public)
        rootBuilder.addExtension(Extension.basicConstraints, true, BasicConstraints(true))
        rootBuilder.addExtension(Extension.keyUsage, true, KeyUsage(KeyUsage.keyCertSign))
        val root = JcaX509CertificateConverter().getCertificate(rootBuilder.build(JcaContentSignerBuilder("SHA256withECDSA").build(rootKey.private)))
        val keys = key()
        val deviceId = UUID.randomUUID()
        val builder = JcaX509v3CertificateBuilder(rootName, BigInteger.TWO,
            Date.from(now.minusSeconds(60)), Date.from(now.plusSeconds(86400)), X500Name("CN=Device"), keys.public)
        builder.addExtension(Extension.basicConstraints, true, BasicConstraints(kind == "ca"))
        builder.addExtension(Extension.keyUsage, true, KeyUsage(KeyUsage.digitalSignature))
        builder.addExtension(Extension.extendedKeyUsage, true, ExtendedKeyUsage(
            if (kind == "server") KeyPurposeId.id_kp_serverAuth else KeyPurposeId.id_kp_clientAuth))
        val names = mutableListOf(GeneralName(GeneralName.uniformResourceIdentifier, "urn:fleet:device:$deviceId"))
        if (kind == "extra-san") names.add(GeneralName(GeneralName.dNSName, "untrusted.test"))
        builder.addExtension(Extension.subjectAlternativeName, false, GeneralNames(names.toTypedArray()))
        val certificate = JcaX509CertificateConverter().getCertificate(builder.build(JcaContentSignerBuilder("SHA256withECDSA").build(rootKey.private)))
        return Fixture(keys, root, EnrollmentCertificateBundle(deviceId, encode(certificate), emptyList(),
            certificate.notBefore.toInstant(), certificate.notAfter.toInstant()))
    }

    private fun key(): KeyPair = KeyPairGenerator.getInstance("EC").apply { initialize(ECGenParameterSpec("secp256r1")) }.generateKeyPair()
    private fun encode(certificate: X509Certificate): String = Base64.getEncoder().encodeToString(certificate.encoded)
}