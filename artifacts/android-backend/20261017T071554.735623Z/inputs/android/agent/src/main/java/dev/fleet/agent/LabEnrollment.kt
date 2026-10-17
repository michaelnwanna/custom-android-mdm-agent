package dev.fleet.agent

import android.app.admin.DevicePolicyManager
import android.content.Context
import java.io.File
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.time.Instant
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

object LabEnrollment {
    private val running = AtomicBoolean(false)
    private val executor = Executors.newSingleThreadExecutor()

    fun start(context: Context) {
        val application = context.applicationContext
        if (!application.getSystemService(DevicePolicyManager::class.java).isDeviceOwnerApp(application.packageName)) {
            status(application, "Device Owner required")
            return
        }
        if (!running.compareAndSet(false, true)) return
        status(application, "Preparing")
        executor.execute {
            try {
                val serverRoot = root(File(application.filesDir, "enrollment-server-ca.pem"))
                val deviceRoot = root(File(application.filesDir, "enrollment-device-ca.pem"))
                val identity = AndroidEnrollmentIdentity(application)
                if (identity.restore(deviceRoot, Instant.now())) {
                    status(application, "Identity restored")
                    return@execute
                }
                val staged = File(application.filesDir, "enrollment-bootstrap.json")
                require(staged.length() in 1..4096)
                val bootstrap = EnrollmentBootstrap.parse(staged.readBytes())
                val csr = identity.prepare(bootstrap, serverRoot, deviceRoot)
                require(staged.delete()) { "Could not consume staged bootstrap" }
                status(application, "Redeeming prepared request")
                val response = EnrollmentTransport.redeem(bootstrap, csr, serverRoot)
                identity.complete(response, deviceRoot, Instant.now())
                status(application, "Identity enrolled")
            } catch (failure: EnrollmentHttpException) {
                status(application, "Server rejected enrollment (${failure.status}); request retained")
            } catch (failure: Exception) {
                status(application, "Enrollment unavailable; prepared identity retained")
            } finally {
                running.set(false)
            }
        }
    }

    fun status(context: Context): String = "Enrollment: " +
        context.getSharedPreferences("lab-enrollment", Context.MODE_PRIVATE).getString("status", "Not started")

    private fun status(context: Context, value: String) {
        context.getSharedPreferences("lab-enrollment", Context.MODE_PRIVATE).edit().putString("status", value).apply()
    }

    private fun root(file: File): X509Certificate {
        require(file.length() in 1..32768)
        val certificates = file.inputStream().use { CertificateFactory.getInstance("X.509").generateCertificates(it) }
        require(certificates.size == 1)
        return certificates.single() as X509Certificate
    }
}