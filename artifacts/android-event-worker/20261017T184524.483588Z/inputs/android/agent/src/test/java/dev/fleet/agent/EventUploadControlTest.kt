package dev.fleet.agent

import org.junit.Assert.*
import org.junit.Test
import java.io.InterruptedIOException

class EventUploadControlTest {
    @Test fun cancellationBeforeAttachOrCommitFailsClosed() {
        val control = EventUploadControl()
        control.cancel()
        assertThrows(InterruptedIOException::class.java) { control.attach { fail("must not attach") } }
        assertThrows(InterruptedIOException::class.java) { control.whileActive { fail("must not mutate") } }
    }

    @Test fun cancellationDisconnectsOnceAndDetachmentDoesNotCancelCompletedWork() {
        val control = EventUploadControl()
        var disconnected = 0
        control.attach { disconnected++ }
        control.cancel()
        control.cancel()
        assertEquals(1, disconnected)
        val completed = EventUploadControl()
        completed.attach { fail("completed connection must be detached") }
        completed.detach()
        assertEquals("committed", completed.whileActive { "committed" })
        completed.cancel()
    }

    @Test fun interruptedThreadCannotAttachOrMutate() {
        val control = EventUploadControl()
        Thread.currentThread().interrupt()
        try {
            assertThrows(InterruptedIOException::class.java) { control.attach { } }
            assertThrows(InterruptedIOException::class.java) { control.whileActive { fail("must not mutate") } }
        } finally {
            Thread.interrupted()
        }
    }
}