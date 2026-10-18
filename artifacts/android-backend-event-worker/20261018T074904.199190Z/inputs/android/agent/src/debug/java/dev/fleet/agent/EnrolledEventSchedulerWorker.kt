package dev.fleet.agent

import android.content.Context
import android.os.SystemClock
import androidx.room.Room
import androidx.work.Worker
import androidx.work.WorkerParameters
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.io.File
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.time.Instant
import java.util.Base64
import java.util.UUID

class EnrolledEventSchedulerWorker(context: Context, private val parameters: WorkerParameters) : Worker(context, parameters) {
    @Volatile private var delegate: EventUploadWorker? = null

    override fun doWork(): Result {
        val nonce = inputData.getString("fixture") ?: return Result.failure()
        if (runCatching { UUID.fromString(nonce).toString() }.getOrNull() != nonce) return Result.failure()
        val directory = File(applicationContext.noBackupFilesDir, "event-scheduler-$nonce")
        val configuration = JSONObject(File(directory, "authorized.json").readText())
        val namespace = configuration.getString("namespace")
        val databaseName = configuration.getString("databaseName")
        require(namespace.matches(Regex("integration-[0-9a-f-]{36}")))
        require(databaseName.matches(Regex("test-mqtt-command-[0-9a-f-]{36}\\.db")))
        fun root(name: String) = CertificateFactory.getInstance("X.509").generateCertificate(
            ByteArrayInputStream(Base64.getDecoder().decode(configuration.getString(name)))) as X509Certificate
        val started = SystemClock.elapsedRealtime()
        val worker = EventUploadWorker(applicationContext, parameters) {
            val serverRoot = root("serverRootDer")
            val deviceRoot = root("deviceRootDer")
            val identity = AndroidEnrollmentIdentity(applicationContext, namespace)
                .tlsIdentity(serverRoot, deviceRoot, Instant.now())
            EventDeliverySession(Room.databaseBuilder(applicationContext, CommandDatabase::class.java, databaseName).build(),
                identity, serverRoot)
        }
        delegate = worker
        if (isStopped) worker.onStopped()
        val result = worker.doWork()
        val record = File(directory, "attempt-$runAttemptCount.json")
        check(record.createNewFile())
        record.writeText(JSONObject().put("workId", id.toString()).put("attempt", runAttemptCount)
            .put("startedMillis", started).put("finishedMillis", SystemClock.elapsedRealtime())
            .put("result", when (result) {
                Result.retry() -> "retry"
                Result.success(androidx.work.Data.Builder().putInt("delivered", 1).build()) -> "delivered"
                else -> "other"
            }).toString())
        return result
    }

    override fun onStopped() {
        delegate?.onStopped()
    }
}