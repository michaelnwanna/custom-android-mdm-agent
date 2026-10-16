package dev.fleet.agent

import android.app.ActivityOptions
import android.app.admin.DevicePolicyManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.provider.Settings

object LabKioskController {
    private const val SAMPLE_PACKAGE = "dev.fleet.sample"

    private fun preferences(context: Context) =
        context.getSharedPreferences("lab-kiosk", Context.MODE_PRIVATE)

    fun status(context: Context): String {
        val preferences = preferences(context)
        val enabled = preferences.getBoolean("enabled", false)
        val boot = preferences.getString("bootStatus", "No boot restoration observed.")
        return "Kiosk intent enabled: $enabled\n$boot"
    }

    fun enable(context: Context) {
        check(preferences(context).edit().putBoolean("enabled", true).commit()) {
            "Could not persist kiosk intent"
        }
        try {
            launch(context)
        } catch (exception: RuntimeException) {
            disable(context)
            throw exception
        }
    }

    fun disable(context: Context) {
        check(preferences(context).edit().putBoolean("enabled", false).commit()) {
            "Could not persist kiosk exit"
        }
        val policy = context.getSystemService(DevicePolicyManager::class.java)
        if (policy.isDeviceOwnerApp(context.packageName)) {
            policy.setLockTaskPackages(ComponentName(context, FleetAdminReceiver::class.java), emptyArray())
        }
    }

    private fun launch(context: Context) {
        val policy = context.getSystemService(DevicePolicyManager::class.java)
        check(policy.isDeviceOwnerApp(context.packageName)) { "Device Owner provisioning required" }
        val intent = context.packageManager.getLaunchIntentForPackage(SAMPLE_PACKAGE)
            ?: error("Sample application is not installed")
        val admin = ComponentName(context, FleetAdminReceiver::class.java)
        policy.setLockTaskPackages(admin, arrayOf(context.packageName, SAMPLE_PACKAGE))
        policy.setLockTaskFeatures(admin, DevicePolicyManager.LOCK_TASK_FEATURE_NONE)
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        val options = ActivityOptions.makeBasic().apply { setLockTaskEnabled(true) }
        context.startActivity(intent, options.toBundle())
    }

    fun boot(context: Context) {
        val preferences = preferences(context)
        val bootCount = Settings.Global.getInt(context.contentResolver, Settings.Global.BOOT_COUNT, -1)
        val message = if (!preferences.getBoolean("enabled", false)) {
            "Kiosk disabled; no boot launch requested."
        } else {
            try {
                launch(context)
                "Kiosk launch requested after boot."
            } catch (exception: RuntimeException) {
                "Kiosk boot restoration failed: ${exception.javaClass.simpleName}"
            }
        }
        preferences.edit().putInt("bootCount", bootCount).putString("bootStatus", message).commit()
    }
}

class LabBootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
            LabKioskController.boot(context)
        }
    }
}