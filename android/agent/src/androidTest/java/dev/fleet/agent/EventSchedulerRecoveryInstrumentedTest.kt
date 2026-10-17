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
class EventSchedulerRecoveryInstrumentedTest {
    private fun nonce(): String = requireNotNull(InstrumentationRegistry.getArguments().getString("probe")).also {
        require(it == UUID.fromString(it).toString())
    }

    @Test fun preparePersistedWorkBeforeHostForceStop() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val nonce = nonce()
        val directory = File(context.noBackupFilesDir, "scheduler-probe-$nonce")
        check(directory.mkdir())
        check(File(directory, "authorized").createNewFile())
        val request = OneTimeWorkRequest.Builder(SchedulerProbeWorker::class.java)
            .setInputData(Data.Builder().putString("probe", nonce).build())
            .setInitialDelay(45, TimeUnit.SECONDS)
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS).build()
        val manager = WorkManager.getInstance(context)
        manager.enqueueUniqueWork("test-event-recovery-$nonce", ExistingWorkPolicy.KEEP, request)
            .result.get(10, TimeUnit.SECONDS)
        assertEquals(WorkInfo.State.ENQUEUED, manager.getWorkInfoById(request.id).get(10, TimeUnit.SECONDS)!!.state)
        assertFalse(directory.listFiles()!!.any { it.name.startsWith("attempt-") })
        File(directory, "prepared.json").writeText(JSONObject().put("schemaVersion", 1)
            .put("probe", nonce).put("workId", request.id.toString()).put("processId", android.os.Process.myPid())
            .put("bootCount", android.provider.Settings.Global.getInt(context.contentResolver, "boot_count"))
            .put("elapsedMillis", android.os.SystemClock.elapsedRealtime()).toString())
    }

    @Test fun resumePersistedWorkAfterHostVerifiedForceStop() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val nonce = nonce()
        val directory = File(context.noBackupFilesDir, "scheduler-probe-$nonce")
        val prepared = JSONObject(File(directory, "prepared.json").readText())
        val id = UUID.fromString(prepared.getString("workId"))
        val manager = WorkManager.getInstance(context)
        val observed = manager.getWorkInfoByIdLiveData(id)
        val finished = CountDownLatch(1)
        var terminal: WorkInfo? = null
        val observer = Observer<WorkInfo?> { info ->
            if (info != null && info.state.isFinished) {
                terminal = info
                finished.countDown()
            }
        }
        try {
            val bootCount = android.provider.Settings.Global.getInt(context.contentResolver, "boot_count")
            if (InstrumentationRegistry.getArguments().getString("reboot") == "true") {
                assertTrue(bootCount > prepared.getInt("bootCount"))
            } else {
                assertEquals(prepared.getInt("bootCount"), bootCount)
                assertNotEquals(prepared.getInt("processId"), android.os.Process.myPid())
            }
            val stored = manager.getWorkInfosForUniqueWork("test-event-recovery-$nonce").get(10, TimeUnit.SECONDS)
            assertEquals(listOf(id), stored.map { it.id })
            instrumentation.runOnMainSync { observed.observeForever(observer) }
            check(finished.await(110, TimeUnit.SECONDS)) { "Persisted work did not recover after explicit relaunch" }
            assertEquals(WorkInfo.State.SUCCEEDED, terminal!!.state)
            val first = JSONObject(File(directory, "attempt-0.json").readText())
            val second = JSONObject(File(directory, "attempt-1.json").readText())
            assertEquals(id.toString(), first.getString("workId"))
            assertEquals(id.toString(), second.getString("workId"))
            assertEquals(0, first.getInt("attempt"))
            assertEquals(1, second.getInt("attempt"))
            assertEquals(android.os.Process.myPid(), first.getInt("processId"))
            assertEquals(android.os.Process.myPid(), second.getInt("processId"))
            assertTrue(second.getLong("elapsedMillis") - first.getLong("elapsedMillis") >= 29000)
            assertEquals(2, directory.listFiles()!!.count { it.name.startsWith("attempt-") })
            File(directory, "verified.json").writeText(JSONObject().put("schemaVersion", 1).put("probe", nonce)
                .put("workId", id.toString()).put("preparedProcessId", prepared.getInt("processId"))
                .put("preparedBootCount", prepared.getInt("bootCount")).put("recoveredBootCount", bootCount)
                .put("recoveredProcessId", android.os.Process.myPid()).put("state", "SUCCEEDED")
                .put("first", first).put("second", second).toString())
        } finally {
            instrumentation.runOnMainSync { observed.removeObserver(observer) }
            manager.cancelUniqueWork("test-event-recovery-$nonce").result.get(10, TimeUnit.SECONDS)
            check(manager.getWorkInfosForUniqueWork("test-event-recovery-$nonce").get(10, TimeUnit.SECONDS)
                .all { it.state.isFinished })
        }
    }

    @Test fun cleanupOnlyThisRecoveryProbe() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val nonce = nonce()
        val manager = WorkManager.getInstance(context)
        manager.cancelUniqueWork("test-event-recovery-$nonce").result.get(10, TimeUnit.SECONDS)
        check(manager.getWorkInfosForUniqueWork("test-event-recovery-$nonce").get(10, TimeUnit.SECONDS)
            .all { it.state.isFinished })
        check(File(context.noBackupFilesDir, "scheduler-probe-$nonce").deleteRecursively())
    }
}