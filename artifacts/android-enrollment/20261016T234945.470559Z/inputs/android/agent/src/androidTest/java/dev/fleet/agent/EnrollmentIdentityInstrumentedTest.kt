package dev.fleet.agent

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
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
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.math.BigInteger
import java.net.URI
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.time.Instant
import java.time.temporal.ChronoUnit
import java.util.Base64
import java.util.Date
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class EnrollmentIdentityInstrumentedTest {
    @Test fun nonExportableKeyExactCsrAndValidatedCertificateSurviveIdentityRecreation() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val namespace = "test-${UUID.randomUUID()}"
        val alias = "fleet-enrollment-$namespace"
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        val now = Instant.now().truncatedTo(ChronoUnit.SECONDS)
        val rootKeys = KeyPairGenerator.getInstance("EC").apply { initialize(ECGenParameterSpec("secp256r1")) }.generateKeyPair()
        val rootName = X500Name("CN=Instrumented enrollment root")
        val rootBuilder = JcaX509v3CertificateBuilder(rootName, BigInteger.ONE, Date.from(now.minusSeconds(60)),
            Date.from(now.plusSeconds(604800)), rootName, rootKeys.public)
        rootBuilder.addExtension(Extension.basicConstraints, true, BasicConstraints(true))
        rootBuilder.addExtension(Extension.keyUsage, true, KeyUsage(KeyUsage.keyCertSign))
        val signer = JcaContentSignerBuilder("SHA256withECDSA").build(rootKeys.private)
        val root = JcaX509CertificateConverter().getCertificate(rootBuilder.build(signer))
        val bootstrap = EnrollmentBootstrap(URI("https://fleet.example.test/api/v1/enrollments"), UUID.randomUUID(), "A".repeat(43))
        try {
            val identity = AndroidEnrollmentIdentity(context, namespace)
            val csr = identity.prepare(bootstrap, root, root)
            assertNull(keyStore.getKey(alias, null).encoded)
            assertEquals(csr, AndroidEnrollmentIdentity(context, namespace).prepare(bootstrap, root, root))
            assertThrows(Exception::class.java) {
                identity.prepare(EnrollmentBootstrap(bootstrap.endpoint, UUID.randomUUID(), bootstrap.token), root, root)
            }
            val publicKey = keyStore.getCertificate(alias).publicKey
            val deviceId = UUID.randomUUID()
            val leafBuilder = JcaX509v3CertificateBuilder(rootName, BigInteger.TWO,
                Date.from(now.minusSeconds(30)), Date.from(now.plusSeconds(86400)), X500Name("CN=Device"), publicKey)
            leafBuilder.addExtension(Extension.basicConstraints, true, BasicConstraints(false))
            leafBuilder.addExtension(Extension.keyUsage, true, KeyUsage(KeyUsage.digitalSignature))
            leafBuilder.addExtension(Extension.extendedKeyUsage, true, ExtendedKeyUsage(KeyPurposeId.id_kp_clientAuth))
            leafBuilder.addExtension(Extension.subjectAlternativeName, false,
                GeneralNames(GeneralName(GeneralName.uniformResourceIdentifier, "urn:fleet:device:$deviceId")))
            val leaf = JcaX509CertificateConverter().getCertificate(leafBuilder.build(signer))
            val response = JSONObject().put("schemaVersion", 1).put("deviceId", deviceId.toString())
                .put("certificateDer", Base64.getEncoder().encodeToString(leaf.encoded))
                .put("intermediateCertificatesDer", JSONArray()).put("notBefore", leaf.notBefore.toInstant().toString())
                .put("notAfter", leaf.notAfter.toInstant().toString()).toString().toByteArray()
            identity.complete(response, root, now)
            assertTrue(AndroidEnrollmentIdentity(context, namespace).restore(root, now))
            assertArrayEquals(leaf.encoded, keyStore.getCertificate(alias).encoded)
            assertNull(keyStore.getKey(alias, null).encoded)
            val proof = Signature.getInstance("SHA256withECDSA").run {
                initSign(keyStore.getKey(alias, null) as java.security.PrivateKey)
                update(byteArrayOf(1, 2, 3))
                sign()
            }
            assertTrue(Signature.getInstance("SHA256withECDSA").run {
                initVerify(leaf.publicKey)
                update(byteArrayOf(1, 2, 3))
                verify(proof)
            })
            assertEquals(csr, AndroidEnrollmentIdentity(context, namespace).prepare(bootstrap, root, root))
            val replacementId = UUID.randomUUID()
            val replacementBuilder = JcaX509v3CertificateBuilder(rootName, BigInteger.valueOf(3),
                Date.from(now.minusSeconds(30)), Date.from(now.plusSeconds(86400)), X500Name("CN=Replacement"), publicKey)
            replacementBuilder.addExtension(Extension.basicConstraints, true, BasicConstraints(false))
            replacementBuilder.addExtension(Extension.keyUsage, true, KeyUsage(KeyUsage.digitalSignature))
            replacementBuilder.addExtension(Extension.extendedKeyUsage, true, ExtendedKeyUsage(KeyPurposeId.id_kp_clientAuth))
            replacementBuilder.addExtension(Extension.subjectAlternativeName, false,
                GeneralNames(GeneralName(GeneralName.uniformResourceIdentifier, "urn:fleet:device:$replacementId")))
            val replacement = JcaX509CertificateConverter().getCertificate(replacementBuilder.build(signer))
            val replacementResponse = JSONObject(response.toString(Charsets.UTF_8)).put("deviceId", replacementId.toString())
                .put("certificateDer", Base64.getEncoder().encodeToString(replacement.encoded)).toString().toByteArray()
            assertThrows(Exception::class.java) { identity.complete(replacementResponse, root, now) }
            assertArrayEquals(leaf.encoded, keyStore.getCertificate(alias).encoded)
            keyStore.deleteEntry(alias)
            assertThrows(Exception::class.java) { AndroidEnrollmentIdentity(context, namespace).prepare(bootstrap, root, root) }
            assertFalse(keyStore.containsAlias(alias))
        } finally {
            if (keyStore.containsAlias(alias)) keyStore.deleteEntry(alias)
            File(context.noBackupFilesDir, "enrollment-$namespace").deleteRecursively()
        }
    }
}