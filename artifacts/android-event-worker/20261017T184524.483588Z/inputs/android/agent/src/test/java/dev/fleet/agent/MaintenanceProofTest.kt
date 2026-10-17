package dev.fleet.agent

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.security.KeyPairGenerator
import java.security.Signature
import java.security.spec.ECGenParameterSpec

class MaintenanceProofTest {
    private val key = KeyPairGenerator.getInstance("EC").apply {
        initialize(ECGenParameterSpec("secp256r1"))
    }.generateKeyPair()
    private val challenge = "fleet-lab-maintenance-v1\ndev.fleet.agent\nrandom-challenge\n".toByteArray()
    private val signature = Signature.getInstance("SHA256withECDSA").run {
        initSign(key.private)
        update(challenge)
        sign()
    }

    @Test fun acceptsFreshProof() {
        assertTrue(MaintenanceProof.verify(key.public.encoded, challenge, signature, 100, 200, 7, 7))
    }

    @Test fun rejectsModifiedChallenge() {
        assertFalse(MaintenanceProof.verify(key.public.encoded, "other".toByteArray(), signature, 100, 200, 7, 7))
    }

    @Test fun rejectsWrongKey() {
        val other = KeyPairGenerator.getInstance("EC").apply { initialize(256) }.generateKeyPair()
        assertFalse(MaintenanceProof.verify(other.public.encoded, challenge, signature, 100, 200, 7, 7))
    }

    @Test fun rejectsExpiredProof() {
        assertFalse(MaintenanceProof.verify(key.public.encoded, challenge, signature, 100, 120101, 7, 7))
    }

    @Test fun rejectsClockReset() {
        assertFalse(MaintenanceProof.verify(key.public.encoded, challenge, signature, 100, 99, 7, 7))
    }

    @Test fun rejectsPreviousBoot() {
        assertFalse(MaintenanceProof.verify(key.public.encoded, challenge, signature, 100, 200, 7, 8))
    }

    @Test fun rejectsUnknownBoot() {
        assertFalse(MaintenanceProof.verify(key.public.encoded, challenge, signature, 100, 200, -1, -1))
    }

    @Test fun rejectsMalformedSignature() {
        assertFalse(MaintenanceProof.verify(key.public.encoded, challenge, ByteArray(16), 100, 200, 7, 7))
    }
}