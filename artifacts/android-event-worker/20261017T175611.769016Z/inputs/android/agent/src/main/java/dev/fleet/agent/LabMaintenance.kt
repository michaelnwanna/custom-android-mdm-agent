package dev.fleet.agent

import android.content.Context
import android.os.SystemClock
import android.provider.Settings
import java.security.SecureRandom
import java.util.Base64

object LabMaintenance {
    private fun preferences(context: Context) =
        context.getSharedPreferences("lab-maintenance", Context.MODE_PRIVATE)

    private fun bootCount(context: Context) =
        Settings.Global.getInt(context.contentResolver, Settings.Global.BOOT_COUNT, -1)

    fun status(context: Context): String =
        preferences(context).getString("status", "Maintenance requires a paired operator key.")!!

    private fun record(context: Context, message: String) {
        check(preferences(context).edit().putString("status", message).commit())
    }

    fun request(context: Context) {
        try {
            val publicKey = context.filesDir.resolve("maintenance-public.der")
            check(publicKey.isFile && publicKey.length() in 32..512) { "Operator key is not paired" }
            val nonce = ByteArray(32).also { SecureRandom().nextBytes(it) }
            val challenge = "fleet-lab-maintenance-v1\n${context.packageName}\n" +
                Base64.getUrlEncoder().withoutPadding().encodeToString(nonce) + "\n"
            val preferences = preferences(context)
            check(preferences.edit().putString("challenge", challenge)
                .putLong("issuedAt", SystemClock.elapsedRealtime())
                .putInt("issuedBoot", bootCount(context)).commit())
            context.filesDir.resolve("maintenance-response.sig").delete()
            context.filesDir.resolve("maintenance-challenge.txt").writeText(challenge)
            record(context, "Maintenance challenge ready; expires in 120 seconds.")
        } catch (exception: Exception) {
            preferences(context).edit().remove("challenge").commit()
            record(context, "Maintenance denied: challenge unavailable.")
        }
    }

    fun authorizeExit(context: Context): Boolean {
        val preferences = preferences(context)
        val challenge = preferences.getString("challenge", null)
        val issuedAt = preferences.getLong("issuedAt", -1)
        val issuedBoot = preferences.getInt("issuedBoot", -1)
        if (!preferences.edit().remove("challenge").commit()) return false
        val response = context.filesDir.resolve("maintenance-response.sig")
        val accepted = try {
            challenge != null && response.isFile && response.length() in 8..128 &&
                MaintenanceProof.verify(
                    context.filesDir.resolve("maintenance-public.der").readBytes(),
                    challenge.toByteArray(Charsets.UTF_8), response.readBytes(), issuedAt,
                    SystemClock.elapsedRealtime(), issuedBoot, bootCount(context)
                )
        } catch (exception: Exception) {
            false
        }
        response.delete()
        context.filesDir.resolve("maintenance-challenge.txt").delete()
        record(context, if (accepted) "Maintenance exit authenticated." else "Maintenance denied: valid fresh operator approval required.")
        return accepted
    }
}