package dev.fleet.agent

import java.io.IOException
import javax.net.ssl.SSLException

internal object EventDeliveryPolicy {
    const val MAXIMUM_EVENTS = 20
    const val MAXIMUM_ATTEMPTS = 5
    const val REQUEST_DEADLINE_SECONDS = 30L

    fun retryable(failure: Exception): Boolean = when (failure) {
        is DeviceHttpException -> failure.status in setOf(408, 425, 429, 500, 502, 503, 504)
        is SSLException -> false
        is IOException -> true
        else -> false
    }

    fun canRetry(runAttemptCount: Int): Boolean = runAttemptCount in 0 until MAXIMUM_ATTEMPTS - 1
}