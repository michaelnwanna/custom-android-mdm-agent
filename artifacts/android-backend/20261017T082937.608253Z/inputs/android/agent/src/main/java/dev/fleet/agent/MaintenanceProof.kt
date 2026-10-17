package dev.fleet.agent

import java.security.KeyFactory
import java.security.Signature
import java.security.spec.X509EncodedKeySpec

object MaintenanceProof {
    fun verify(
        publicKey: ByteArray,
        challenge: ByteArray,
        signature: ByteArray,
        issuedAt: Long,
        now: Long,
        issuedBoot: Int,
        currentBoot: Int
    ): Boolean {
        if (issuedBoot < 0 || issuedBoot != currentBoot || issuedAt < 0 || now < issuedAt ||
            now - issuedAt > 120_000 || signature.size !in 8..128 || publicKey.size !in 32..512
        ) return false
        return try {
            val key = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(publicKey))
            Signature.getInstance("SHA256withECDSA").run {
                initVerify(key)
                update(challenge)
                verify(signature)
            }
        } catch (exception: java.security.GeneralSecurityException) {
            false
        }
    }
}