package dev.fleet.agent

import android.content.Context
import android.os.SystemClock
import androidx.work.Worker
import androidx.work.WorkerParameters
import org.json.JSONObject
import java.io.File

class SchedulerProbeWorker(context: Context, parameters: WorkerParameters) : Worker(context, parameters) {
    override fun doWork(): Result {
        val nonce = inputData.getString("probe") ?: return Result.failure()
        if (!nonce.matches(Regex("[0-9a-f-]{36}"))) return Result.failure()
        val directory = File(applicationContext.noBackupFilesDir, "scheduler-probe-$nonce")
        if (!File(directory, "authorized").isFile) return Result.failure()
        val record = File(directory, "attempt-$runAttemptCount.json")
        if (!record.createNewFile()) return Result.failure()
        record.writeText(JSONObject().put("attempt", runAttemptCount)
            .put("elapsedMillis", SystemClock.elapsedRealtime()).put("workId", id.toString()).toString())
        return if (runAttemptCount == 0) Result.retry() else Result.success()
    }
}