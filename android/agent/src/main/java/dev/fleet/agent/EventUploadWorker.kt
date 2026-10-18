package dev.fleet.agent

import android.app.Application
import android.app.admin.DevicePolicyManager
import android.content.Context
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.Data
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequest
import androidx.work.WorkManager
import androidx.work.Worker
import androidx.work.WorkerParameters
import java.io.File
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.time.Instant
import java.util.concurrent.TimeUnit

class EventDeliveryApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        EventUploadScheduler.scheduleExisting(this)
    }
}

internal object EventUploadScheduler {
    const val UNIQUE_NAME = "fleet-command-event-upload"

    fun request(): OneTimeWorkRequest = OneTimeWorkRequest.Builder(EventUploadWorker::class.java)
        .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
        .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS)
        .build()

    fun enqueue(context: Context) = WorkManager.getInstance(context).enqueueUniqueWork(
        UNIQUE_NAME, ExistingWorkPolicy.KEEP, request())

    fun cancel(context: Context) = WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_NAME)

    fun scheduleExisting(context: Context) {
        if (File(context.noBackupFilesDir, "enrollment-device/identity.json").isFile &&
            File(context.filesDir, "enrollment-server-ca.pem").isFile &&
            File(context.filesDir, "enrollment-device-ca.pem").isFile) enqueue(context)
    }
}

internal class EventDeliverySession(
    val database: CommandDatabase,
    val policyDatabase: PolicyDatabase,
    val identity: DeviceTlsIdentity,
    val serverRoot: X509Certificate,
) : java.io.Closeable {
    val journal = CommandJournal(database, identity.deviceId)
    val policyHandler = PolicyHandler(policyDatabase, identity.deviceId)

    override fun close() {
        database.close()
        policyDatabase.close()
    }

    companion object {
        fun open(context: Context): EventDeliverySession {
            require(context.getSystemService(DevicePolicyManager::class.java).isDeviceOwnerApp(context.packageName))
            val serverRoot = root(File(context.filesDir, "enrollment-server-ca.pem"))
            val deviceRoot = root(File(context.filesDir, "enrollment-device-ca.pem"))
            val identity = AndroidEnrollmentIdentity(context).tlsIdentity(serverRoot, deviceRoot, Instant.now())
            return EventDeliverySession(
                CommandDatabase.open(context),
                PolicyDatabase.open(context),
                identity,
                serverRoot
            )
        }

        private fun root(file: File): X509Certificate {
            require(file.length() in 1..32768)
            val certificates = file.inputStream().use { CertificateFactory.getInstance("X.509").generateCertificates(it) }
            require(certificates.size == 1)
            return certificates.single() as X509Certificate
        }
    }
}

class EventUploadWorker internal constructor(
    context: Context,
    parameters: WorkerParameters,
    private val openSession: () -> EventDeliverySession,
) : Worker(context, parameters) {
    constructor(context: Context, parameters: WorkerParameters) : this(context, parameters,
        { EventDeliverySession.open(context.applicationContext) })

    private val control = EventUploadControl()

    override fun doWork(): Result {
        if (isStopped || runAttemptCount >= EventDeliveryPolicy.MAXIMUM_ATTEMPTS) return Result.failure()
        return try {
            openSession().use { session ->
                val events = control.whileActive { session.journal.pendingEvents(EventDeliveryPolicy.MAXIMUM_EVENTS) }
                for (event in events) {
                    control.whileActive { check(!isStopped) }
                    session.journal.uploadAndAcknowledge(session.identity, session.serverRoot, event, control)
                }
                control.whileActive {
                    if (session.journal.pendingEvents(1).isEmpty())
                        Result.success(Data.Builder().putInt("delivered", events.size).build())
                    else retryOrFail()
                }
            }
        } catch (failure: Exception) {
            if (!isStopped && (control.isCancelled || EventDeliveryPolicy.retryable(failure))) retryOrFail()
            else Result.failure()
        }
    }

    private fun retryOrFail(): Result = if (EventDeliveryPolicy.canRetry(runAttemptCount)) Result.retry() else Result.failure()

    override fun onStopped() {
        control.cancel()
    }
}