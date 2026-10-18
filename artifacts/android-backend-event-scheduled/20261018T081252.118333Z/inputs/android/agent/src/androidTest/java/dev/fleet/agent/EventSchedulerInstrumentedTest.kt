package dev.fleet.agent

import androidx.lifecycle.Observer
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.work.BackoffPolicy
import androidx.work.Data
import androidx.work.ExistingWorkPolicy
import androidx.work.OneTimeWorkRequest
import androidx.work.WorkInfo
import androidx.work.WorkManager
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

@RunWith(AndroidJUnit4::class)
class EventSchedulerInstrumentedTest {
    @Test fun actualDispatchRetriesAfterBackoffWithTheSamePersistedWorkId() = withProbe { manager, nonce, directory ->
        val request = request(nonce)
        val name = "test-event-scheduler-$nonce"
        manager.enqueueUniqueWork(name, ExistingWorkPolicy.KEEP, request).result.get(10, TimeUnit.SECONDS)
        val duplicate = request(nonce)
        manager.enqueueUniqueWork(name, ExistingWorkPolicy.KEEP, duplicate).result.get(10, TimeUnit.SECONDS)
        assertEquals(listOf(request.id), manager.getWorkInfosForUniqueWork(name).get(10, TimeUnit.SECONDS).map { it.id })
        val terminal = awaitTerminal(manager, request.id, 100)
        assertEquals(WorkInfo.State.SUCCEEDED, terminal.state)
        val first = JSONObject(File(directory, "attempt-0.json").readText())
        val second = JSONObject(File(directory, "attempt-1.json").readText())
        assertEquals(request.id.toString(), first.getString("workId"))
        assertEquals(first.getString("workId"), second.getString("workId"))
        assertEquals(0, first.getInt("attempt"))
        assertEquals(1, second.getInt("attempt"))
        val delay = second.getLong("elapsedMillis") - first.getLong("elapsedMillis")
        assertTrue("Backoff executed too early: $delay", delay >= 29000)
        assertEquals(2, directory.listFiles()!!.count { it.name.startsWith("attempt-") })
    }

    @Test fun cancellingDelayedWorkPersistsCancelledStateWithoutDispatch() = withProbe { manager, nonce, directory ->
        val request = OneTimeWorkRequest.Builder(SchedulerProbeWorker::class.java)
            .setInputData(Data.Builder().putString("probe", nonce).build())
            .setInitialDelay(10, TimeUnit.MINUTES).build()
        manager.enqueueUniqueWork("test-event-scheduler-$nonce", ExistingWorkPolicy.KEEP, request)
            .result.get(10, TimeUnit.SECONDS)
        assertEquals(WorkInfo.State.ENQUEUED, manager.getWorkInfoById(request.id).get(10, TimeUnit.SECONDS)!!.state)
        manager.cancelWorkById(request.id).result.get(10, TimeUnit.SECONDS)
        assertEquals(WorkInfo.State.CANCELLED, awaitTerminal(manager, request.id, 10).state)
        assertEquals(WorkInfo.State.CANCELLED, manager.getWorkInfoById(request.id).get(10, TimeUnit.SECONDS)!!.state)
        assertFalse(directory.listFiles()!!.any { it.name.startsWith("attempt-") })
    }

    private fun request(nonce: String) = OneTimeWorkRequest.Builder(SchedulerProbeWorker::class.java)
        .setInputData(Data.Builder().putString("probe", nonce).build())
        .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS).build()

    private fun awaitTerminal(manager: WorkManager, id: UUID, seconds: Long): WorkInfo {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val observed = manager.getWorkInfoByIdLiveData(id)
        val finished = CountDownLatch(1)
        var result: WorkInfo? = null
        val observer = Observer<WorkInfo?> { info ->
            if (info != null && info.state.isFinished) {
                result = info
                finished.countDown()
            }
        }
        instrumentation.runOnMainSync { observed.observeForever(observer) }
        try {
            check(finished.await(seconds, TimeUnit.SECONDS)) { "Scheduler did not reach a terminal state" }
            return requireNotNull(result)
        } finally {
            instrumentation.runOnMainSync { observed.removeObserver(observer) }
        }
    }

    private fun withProbe(action: (WorkManager, String, File) -> Unit) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val manager = WorkManager.getInstance(context)
        val nonce = UUID.randomUUID().toString()
        val directory = File(context.noBackupFilesDir, "scheduler-probe-$nonce")
        check(directory.mkdir())
        check(File(directory, "authorized").createNewFile())
        try {
            action(manager, nonce, directory)
        } finally {
            manager.cancelUniqueWork("test-event-scheduler-$nonce").result.get(10, TimeUnit.SECONDS)
            check(manager.getWorkInfosForUniqueWork("test-event-scheduler-$nonce").get(10, TimeUnit.SECONDS)
                .all { it.state.isFinished }) { "Probe work remained active after cleanup" }
            check(directory.deleteRecursively())
        }
    }
}