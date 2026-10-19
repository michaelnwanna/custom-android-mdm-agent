package dev.fleet.agent

import android.app.Activity
import android.app.admin.DevicePolicyManager
import android.content.Context
import android.os.Build
import android.os.Bundle
import android.os.PersistableBundle

/** Answers the setup wizard during QR provisioning: this agent only supports fully managed devices. */
class GetProvisioningModeActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val mode = DevicePolicyManager.PROVISIONING_MODE_FULLY_MANAGED_DEVICE
        val allowed = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S)
            intent.getIntArrayExtra(DevicePolicyManager.EXTRA_PROVISIONING_ALLOWED_PROVISIONING_MODES) else null
        if (allowed == null || mode in allowed) {
            setResult(RESULT_OK, intent.cloneFilter().putExtra(DevicePolicyManager.EXTRA_PROVISIONING_MODE, mode))
        } else {
            setResult(RESULT_CANCELED)
        }
        finish()
    }
}

/**
 * Runs after Android sets this agent as Device Owner. Stages the QR admin extras and starts
 * enrollment; setup always continues so a rejected payload can still be retried from the agent UI.
 */
class PolicyComplianceActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val staged = try {
            val bundle = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU)
                intent.getParcelableExtra(DevicePolicyManager.EXTRA_PROVISIONING_ADMIN_EXTRAS_BUNDLE, PersistableBundle::class.java)
            else
                @Suppress("DEPRECATION") intent.getParcelableExtra(DevicePolicyManager.EXTRA_PROVISIONING_ADMIN_EXTRAS_BUNDLE)
            ProvisioningExtras.parse(requireNotNull(bundle).let { extras -> extras.keySet().associateWith { extras.getString(it) } })
                .stage(filesDir)
            true
        } catch (failure: Exception) {
            getSharedPreferences("lab-enrollment", Context.MODE_PRIVATE).edit()
                .putString("status", "QR provisioning extras rejected").apply()
            false
        }
        setResult(RESULT_OK)
        finish()
        if (staged) LabEnrollment.start(this)
    }
}
