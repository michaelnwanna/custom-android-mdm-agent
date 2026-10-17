package dev.fleet.agent

import java.io.InterruptedIOException

internal class EventUploadControl {
    private val lock = Any()
    private var cancelled = false
    private var disconnect: (() -> Unit)? = null

    val isCancelled: Boolean get() = synchronized(lock) { cancelled }

    fun cancel() {
        val action = synchronized(lock) {
            cancelled = true
            disconnect.also { disconnect = null }
        }
        action?.invoke()
    }

    fun attach(action: () -> Unit) = synchronized(lock) {
        checkActive()
        check(disconnect == null)
        disconnect = action
    }

    fun detach() = synchronized(lock) { disconnect = null }

    fun <Value> whileActive(action: () -> Value): Value = synchronized(lock) {
        checkActive()
        action()
    }

    private fun checkActive() {
        if (cancelled || Thread.currentThread().isInterrupted) throw InterruptedIOException("Event upload cancelled")
    }
}