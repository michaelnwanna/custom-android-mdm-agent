package dev.fleet.agent

import android.app.PendingIntent
import android.app.admin.DevicePolicyManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.os.Build
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.Executors

object LabUpdateInstaller {
    private const val SAMPLE_PACKAGE = "dev.fleet.sample"
    private const val RESULT_ACTION = "dev.fleet.agent.LAB_INSTALL_RESULT"
    private val executor = Executors.newSingleThreadExecutor()

    private fun preferences(context: Context) =
        context.getSharedPreferences("lab-update", Context.MODE_PRIVATE)

    fun status(context: Context): String =
        preferences(context).getString("status", "No lab update attempted.")!!

    @Synchronized
    fun install(context: Context) {
        val application = context.applicationContext
        val preferences = preferences(application)
        if (preferences.getBoolean("busy", false)) return
        preferences.edit().putBoolean("busy", true).putBoolean("pendingUserAction", false)
            .putInt("sessionId", -1).putString("status", "Preparing lab v2 update.").commit()
        executor.execute {
            var sessionId = -1
            try {
                check(application.getSystemService(DevicePolicyManager::class.java)
                    .isDeviceOwnerApp(application.packageName)) { "Device Owner provisioning required" }
                val artifact = File(application.filesDir, "sample-update.apk")
                check(artifact.isFile && artifact.length() in 1..50_000_000L) {
                    "Stage a lab APK in private files/sample-update.apk first"
                }
                val packages = application.packageManager
                val candidate = packages.getPackageArchiveInfo(
                    artifact.absolutePath, PackageManager.GET_SIGNING_CERTIFICATES
                ) ?: error("Invalid APK")
                val installed = packages.getPackageInfo(SAMPLE_PACKAGE, PackageManager.GET_SIGNING_CERTIFICATES)
                check(candidate.packageName == SAMPLE_PACKAGE) { "Wrong package identity" }
                check(installed.longVersionCode == 1L && candidate.longVersionCode == 2L) {
                    "This lab only supports the sample v1-to-v2 transition"
                }
                check(candidate.applicationInfo!!.minSdkVersion <= Build.VERSION.SDK_INT) { "Incompatible minimum SDK" }
                val candidateSigners = candidate.signingInfo?.apkContentsSigners?.map { it.toCharsString() }?.toSet()
                val installedSigners = installed.signingInfo?.apkContentsSigners?.map { it.toCharsString() }?.toSet()
                check(!candidateSigners.isNullOrEmpty() && candidateSigners == installedSigners) { "APK signer mismatch" }
                val digest = artifact.inputStream().use { input ->
                    val hash = MessageDigest.getInstance("SHA-256")
                    val buffer = ByteArray(8192)
                    var count = input.read(buffer)
                    while (count != -1) {
                        hash.update(buffer, 0, count)
                        count = input.read(buffer)
                    }
                    hash.digest().joinToString("") { "%02x".format(it) }
                }
                val installer = packages.packageInstaller
                val parameters = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
                    setAppPackageName(SAMPLE_PACKAGE)
                    setSize(artifact.length())
                    setInstallReason(PackageManager.INSTALL_REASON_POLICY)
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                        setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
                    }
                }
                sessionId = installer.createSession(parameters)
                preferences.edit().putInt("sessionId", sessionId).putString("artifactSha256", digest)
                    .putString("status", "Installing lab v2; session $sessionId.").commit()
                installer.openSession(sessionId).use { session ->
                    session.openWrite("sample.apk", 0, artifact.length()).use { output ->
                        artifact.inputStream().use { it.copyTo(output) }
                        session.fsync(output)
                    }
                    val callback = Intent(application, LabInstallResultReceiver::class.java).setAction(RESULT_ACTION)
                    val result = PendingIntent.getBroadcast(
                        application, sessionId, callback, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE
                    )
                    session.commit(result.intentSender)
                }
            } catch (exception: Exception) {
                if (sessionId >= 0) {
                    runCatching { application.packageManager.packageInstaller.abandonSession(sessionId) }
                }
                preferences.edit().putBoolean("busy", false)
                    .putString("status", "Lab update failed: ${exception.message}").commit()
            }
        }
    }

    fun receive(context: Context, intent: Intent) {
        if (intent.action != RESULT_ACTION) return
        val preferences = preferences(context)
        val sessionId = intent.getIntExtra(PackageInstaller.EXTRA_SESSION_ID, -1)
        if (sessionId < 0 || sessionId != preferences.getInt("sessionId", -1)) return
        val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)
        val message = when (status) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                preferences.edit().putBoolean("pendingUserAction", true).commit()
                "Lab update requires user confirmation; silent-install gate FAILED."
            }
            PackageInstaller.STATUS_SUCCESS -> {
                val actualVersion = runCatching {
                    context.packageManager.getPackageInfo(SAMPLE_PACKAGE, 0).longVersionCode
                }.getOrNull()
                if (actualVersion == 2L && !preferences.getBoolean("pendingUserAction", false)) {
                    "Lab v2 installed silently; observed version code 2."
                } else {
                    "Lab update verification failed; version=$actualVersion."
                }
            }
            else -> "Lab update failed: status $status, ${intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)}"
        }
        preferences.edit().putBoolean("busy", status == PackageInstaller.STATUS_PENDING_USER_ACTION)
            .putInt("installerStatus", status).putString("status", message).commit()
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            runCatching { context.packageManager.packageInstaller.abandonSession(sessionId) }
            preferences.edit().putBoolean("busy", false).commit()
        }
    }
}

class LabInstallResultReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        LabUpdateInstaller.receive(context, intent)
    }
}