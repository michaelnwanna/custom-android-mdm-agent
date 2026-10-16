package dev.fleet.agent

import android.app.Activity
import android.app.ActivityManager
import android.app.admin.DevicePolicyManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

class MainActivity : Activity() {
    private val policyManager by lazy { getSystemService(DevicePolicyManager::class.java) }
    private lateinit var status: TextView
    private val handler = Handler(Looper.getMainLooper())
    private val refresh = object : Runnable {
        override fun run() {
            refreshStatus()
            handler.postDelayed(this, 500)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val padding = (24 * resources.displayMetrics.density).toInt()
            setPadding(padding, padding, padding, padding)
        }
        status = TextView(this).apply { textSize = 18f }
        content.addView(status)
        content.addView(Button(this).apply {
            text = "Start sample kiosk"
            setOnClickListener { startKiosk() }
        })
        content.addView(Button(this).apply {
            text = "Exit lab kiosk"
            setOnClickListener {
                try {
                    LabKioskController.disable(this@MainActivity)
                } catch (exception: RuntimeException) {
                    status.text = "Kiosk exit failed: ${exception.message}"
                }
                refreshStatus()
            }
        })
        content.addView(Button(this).apply {
            text = "Install staged lab v2"
            setOnClickListener { LabUpdateInstaller.install(this@MainActivity) }
        })
        setContentView(content)
    }

    override fun onResume() {
        super.onResume()
        handler.post(refresh)
    }

    override fun onPause() {
        handler.removeCallbacks(refresh)
        super.onPause()
    }

    private fun startKiosk() {
        try {
            LabKioskController.enable(this)
        } catch (exception: RuntimeException) {
            status.text = "Kiosk start failed: ${exception.message}"
        }
    }

    private fun refreshStatus() {
        val owner = policyManager.isDeviceOwnerApp(packageName)
        val locked = getSystemService(ActivityManager::class.java).lockTaskModeState ==
            ActivityManager.LOCK_TASK_MODE_LOCKED
        val message = "Fleet Lab Agent\nDevice Owner: $owner\nLock task locked: $locked\n${LabKioskController.status(this)}\n${LabUpdateInstaller.status(this)}"
        if (status.text.toString() != message) {
            status.text = message
        }
    }
}