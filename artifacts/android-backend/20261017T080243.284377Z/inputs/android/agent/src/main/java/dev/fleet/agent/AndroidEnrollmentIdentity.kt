package dev.fleet.agent

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.AtomicFile
import org.bouncycastle.operator.jcajce.JcaContentVerifierProviderBuilder
import org.bouncycastle.pkcs.PKCS10CertificationRequest
import org.json.JSONObject
import java.io.File
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.cert.X509Certificate
import java.security.spec.ECGenParameterSpec
import java.time.Instant
import java.util.Base64

class AndroidEnrollmentIdentity(context: Context, namespace: String = "device") {
    private val alias: String
    private val journal: AtomicFile
    private val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    init {
        require(namespace.matches(Regex("[A-Za-z0-9-]{1,80}")))
        alias = "fleet-enrollment-$namespace"
        val directory = File(context.noBackupFilesDir, "enrollment-$namespace")
        require(directory.isDirectory || directory.mkdirs())
        journal = AtomicFile(File(directory, "identity.json"))
    }

    fun prepare(bootstrap: EnrollmentBootstrap, serverRoot: X509Certificate, deviceRoot: X509Certificate): String = synchronized(lock) {
        val state = load()
        if (state != null) {
            require(state.getString("pendingId") == bootstrap.pendingId.toString() &&
                state.getString("endpoint") == bootstrap.endpoint.toString() &&
                state.getString("serverRootSha256") == fingerprint(serverRoot) &&
                state.getString("deviceRootSha256") == fingerprint(deviceRoot)) { "Existing enrollment binding differs" }
            val keys = keys()
            val csr = state.getString("csrDer")
            val request = PKCS10CertificationRequest(Base64.getDecoder().decode(csr))
            require(request.subjectPublicKeyInfo.encoded.contentEquals(keys.public.encoded) &&
                request.isSignatureValid(JcaContentVerifierProviderBuilder().build(keys.public)))
            return@synchronized csr
        }
        require(!keyStore.containsAlias(alias)) { "Enrollment key exists without its journal" }
        val keys = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore").apply {
            initialize(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY)
                .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256).setUserAuthenticationRequired(false).build())
        }.generateKeyPair()
        require(keys.private.encoded == null) { "Enrollment key must not be exportable" }
        val csr = EnrollmentProof.createCsr(keys)
        save(JSONObject().put("pendingId", bootstrap.pendingId.toString()).put("endpoint", bootstrap.endpoint.toString())
            .put("serverRootSha256", fingerprint(serverRoot)).put("deviceRootSha256", fingerprint(deviceRoot)).put("csrDer", csr))
        csr
    }

    fun restore(deviceRoot: X509Certificate, now: Instant): Boolean = synchronized(lock) {
        val state = load() ?: return@synchronized false
        if (!state.has("responseBase64")) return@synchronized false
        complete(Base64.getDecoder().decode(state.getString("responseBase64")), deviceRoot, now)
        true
    }

    fun tlsIdentity(serverRoot: X509Certificate, deviceRoot: X509Certificate, now: Instant): DeviceTlsIdentity = synchronized(lock) {
        require(restore(deviceRoot, now)) { "Enrollment must be complete" }
        val state = requireNotNull(load())
        require(state.getString("serverRootSha256") == fingerprint(serverRoot))
        val response = EnrollmentResponse.parse(Base64.getDecoder().decode(state.getString("responseBase64")))
        val keys = keys()
        val chain = EnrollmentProof.verify(response, keys.public, deviceRoot, now)
        DeviceTlsIdentity(java.net.URI(state.getString("endpoint")).resolve("/api/v1/device/identity"),
            response.deviceId, DeviceKeyManager(keys.private, chain + deviceRoot))
    }

    fun complete(bytes: ByteArray, deviceRoot: X509Certificate, now: Instant) = synchronized(lock) {
        val state = requireNotNull(load()) { "Enrollment request was not prepared" }
        require(state.getString("deviceRootSha256") == fingerprint(deviceRoot))
        val response = EnrollmentResponse.parse(bytes)
        val keys = keys()
        val chain = EnrollmentProof.verify(response, keys.public, deviceRoot, now)
        if (state.has("responseBase64")) {
            val original = EnrollmentResponse.parse(Base64.getDecoder().decode(state.getString("responseBase64")))
            require(response == original) { "Completed identity cannot be replaced" }
        }
        state.put("responseBase64", Base64.getEncoder().encodeToString(bytes))
        save(state)
        keyStore.setKeyEntry(alias, keys.private, null, chain.toTypedArray())
    }

    private fun keys(): KeyPair {
        val privateKey = keyStore.getKey(alias, null) as? java.security.PrivateKey
            ?: throw IllegalStateException("Enrollment key is missing; replacement is forbidden")
        require(privateKey.encoded == null)
        return KeyPair(requireNotNull(keyStore.getCertificate(alias)).publicKey, privateKey)
    }

    private fun load(): JSONObject? {
        val bytes = try { journal.readFully() } catch (missing: java.io.FileNotFoundException) { return null }
        require(bytes.size <= 160000)
        return JSONObject(bytes.toString(Charsets.UTF_8))
    }

    private fun save(state: JSONObject) {
        val stream = journal.startWrite()
        try {
            stream.write(state.toString().toByteArray(Charsets.UTF_8))
            journal.finishWrite(stream)
        } catch (failure: Exception) {
            journal.failWrite(stream)
            throw failure
        }
    }

    companion object {
        private val lock = Any()
        private fun fingerprint(certificate: X509Certificate): String =
            Base64.getEncoder().encodeToString(MessageDigest.getInstance("SHA-256").digest(certificate.encoded))
    }
}