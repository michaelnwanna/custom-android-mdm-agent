package com.michaelnwanna.mdm_companion

import android.os.Bundle
import android.view.WindowManager
import io.flutter.embedding.android.FlutterActivity

class MainActivity : FlutterActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Enrollment QR codes carry single-use secrets; block screenshots and recents previews.
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
    }
}
