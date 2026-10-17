package dev.fleet.sample

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Intent
import android.os.Bundle
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

class MainActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val padding = (24 * resources.displayMetrics.density).toInt()
            setPadding(padding, padding, padding, padding)
        }
        val status = TextView(this).apply {
            text = "Fleet Sample\nVersion ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})"
            textSize = 22f
        }
        content.addView(status)
        content.addView(Button(this).apply {
            text = "Lab management"
            setOnClickListener {
                try {
                    startActivity(Intent().setClassName("dev.fleet.agent", "dev.fleet.agent.MainActivity"))
                } catch (exception: ActivityNotFoundException) {
                    status.text = "Fleet Lab Agent is not installed."
                }
            }
        })
        setContentView(content)
    }
}