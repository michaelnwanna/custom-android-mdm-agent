package dev.fleet.agent

import org.bouncycastle.asn1.x500.X500Name
import org.bouncycastle.asn1.x509.AlgorithmIdentifier
import org.bouncycastle.asn1.x9.X9ObjectIdentifiers
import org.bouncycastle.operator.ContentSigner
import org.bouncycastle.pkcs.jcajce.JcaPKCS10CertificationRequestBuilder
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.OutputStream
import java.security.AlgorithmParameters
import java.security.KeyPair
import java.security.PublicKey
import java.security.Signature
import java.security.cert.CertPathValidator
import java.security.cert.CertificateFactory
import java.security.cert.PKIXParameters
import java.security.cert.TrustAnchor
import java.security.cert.X509Certificate
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.time.Duration
import java.time.Instant
import java.util.Base64
import java.util.Date
import java.util.UUID

data class EnrollmentCertificateBundle(
    val deviceId: UUID,
    val certificateDer: String,
    val intermediateCertificatesDer: List<String>,
    val notBefore: Instant,
    val notAfter: Instant
)

object EnrollmentProof {
    fun createCsr(keys: KeyPair): String {
        requireP256(keys.public)
        val signature = Signature.getInstance("SHA256withECDSA").apply { initSign(keys.private) }
        val buffer = ByteArrayOutputStream()
        val signer = object : ContentSigner {
            override fun getAlgorithmIdentifier() = AlgorithmIdentifier(X9ObjectIdentifiers.ecdsa_with_SHA256)
            override fun getOutputStream(): OutputStream = buffer
            override fun getSignature(): ByteArray {
                signature.update(buffer.toByteArray())
                return signature.sign()
            }
        }
        val request = JcaPKCS10CertificationRequestBuilder(X500Name("CN=Fleet enrollment"), keys.public).build(signer)
        return Base64.getEncoder().encodeToString(request.encoded)
    }

    fun verify(
        response: EnrollmentCertificateBundle,
        expectedKey: PublicKey,
        trustedRoot: X509Certificate,
        now: Instant,
        maximumLifetime: Duration = Duration.ofDays(2)
    ): List<X509Certificate> {
        requireP256(expectedKey)
        require(response.deviceId != UUID(0, 0)) { "Invalid enrollment certificate" }
        require(maximumLifetime > Duration.ZERO && maximumLifetime <= Duration.ofDays(30))
        require(response.intermediateCertificatesDer.size <= 4) { "Invalid enrollment chain" }
        val chain = (listOf(response.certificateDer) + response.intermediateCertificatesDer).map(::decodeCertificate)
        require(chain.map { Base64.getEncoder().encodeToString(it.encoded) }.distinct().size == chain.size)
        require(chain.none { it.encoded.contentEquals(trustedRoot.encoded) }) { "Trust roots cannot be returned" }
        val leaf = chain.first()
        require(leaf.publicKey.encoded.contentEquals(expectedKey.encoded)) { "Enrollment key mismatch" }
        require(leaf.sigAlgOID == "1.2.840.10045.4.3.2" && leaf.basicConstraints == -1)
        val usage = leaf.keyUsage
        require(usage != null && usage[0] && usage.drop(1).none { it })
        require(leaf.extendedKeyUsage == listOf("1.3.6.1.5.5.7.3.2"))
        val names = leaf.subjectAlternativeNames
        require(names != null && names.size == 1 && names.first().toList() ==
            listOf(6, "urn:fleet:device:${response.deviceId}")) { "Enrollment identity mismatch" }
        require(response.notBefore == leaf.notBefore.toInstant() && response.notAfter == leaf.notAfter.toInstant())
        require(response.notBefore <= now && now < response.notAfter && response.notAfter > response.notBefore)
        require(Duration.between(response.notBefore, response.notAfter) <= maximumLifetime)
        trustedRoot.checkValidity(Date.from(now))
        require(trustedRoot.basicConstraints >= 0)
        for ((certificate, issuer) in chain.zipWithNext())
            require(certificate.issuerX500Principal == issuer.subjectX500Principal)
        val path = CertificateFactory.getInstance("X.509").generateCertPath(chain)
        val parameters = PKIXParameters(setOf(TrustAnchor(trustedRoot, null))).apply {
            isRevocationEnabled = false
            date = Date.from(now)
        }
        CertPathValidator.getInstance("PKIX").validate(path, parameters)
        return chain
    }

    private fun decodeCertificate(encoded: String): X509Certificate {
        require(encoded.length in 64..32768)
        val bytes = Base64.getDecoder().decode(encoded)
        require(Base64.getEncoder().encodeToString(bytes) == encoded)
        val stream = ByteArrayInputStream(bytes)
        val certificate = CertificateFactory.getInstance("X.509").generateCertificate(stream) as X509Certificate
        require(stream.available() == 0 && certificate.encoded.contentEquals(bytes))
        return certificate
    }

    private fun requireP256(key: PublicKey) {
        val expected = AlgorithmParameters.getInstance("EC").apply { init(ECGenParameterSpec("secp256r1")) }
            .getParameterSpec(ECParameterSpec::class.java)
        val actual = (key as? ECPublicKey)?.params ?: throw IllegalArgumentException("P-256 key required")
        require(actual.curve == expected.curve && actual.generator == expected.generator &&
            actual.order == expected.order && actual.cofactor == expected.cofactor) { "P-256 key required" }
    }
}