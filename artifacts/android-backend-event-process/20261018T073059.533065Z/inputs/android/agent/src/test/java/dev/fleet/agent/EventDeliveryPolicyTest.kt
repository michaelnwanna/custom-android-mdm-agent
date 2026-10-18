package dev.fleet.agent

import org.junit.Assert.*
import org.junit.Test
import java.io.IOException
import java.net.SocketTimeoutException
import javax.net.ssl.SSLHandshakeException

class EventDeliveryPolicyTest {
    @Test fun onlyTransientTransportFailuresAreRetryable() {
        for (status in listOf(408, 425, 429, 500, 502, 503, 504))
            assertTrue(EventDeliveryPolicy.retryable(DeviceHttpException(status)))
        for (status in listOf(200, 301, 400, 401, 403, 404, 409, 413, 415, 501))
            assertFalse(EventDeliveryPolicy.retryable(DeviceHttpException(status)))
        assertTrue(EventDeliveryPolicy.retryable(IOException("unavailable")))
        assertTrue(EventDeliveryPolicy.retryable(SocketTimeoutException()))
        assertFalse(EventDeliveryPolicy.retryable(SSLHandshakeException("untrusted")))
        assertFalse(EventDeliveryPolicy.retryable(IllegalArgumentException("invalid acknowledgement")))
        assertFalse(EventDeliveryPolicy.retryable(IllegalStateException("identity unavailable")))
    }

    @Test fun attemptsAndBatchSizeAreExplicitlyBounded() {
        assertEquals(20, EventDeliveryPolicy.MAXIMUM_EVENTS)
        assertEquals(15L, EventDeliveryPolicy.REQUEST_DEADLINE_SECONDS)
        for (attempt in 0..3) assertTrue(EventDeliveryPolicy.canRetry(attempt))
        for (attempt in listOf(-1, 4, 5, Int.MAX_VALUE)) assertFalse(EventDeliveryPolicy.canRetry(attempt))
    }
}