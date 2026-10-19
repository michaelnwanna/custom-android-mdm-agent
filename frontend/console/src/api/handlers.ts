/**
 * API error handling and utilities
 */

export class ApiError extends Error {
  constructor(
    public statusCode: number,
    public message: string,
    public details?: unknown
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

/**
 * Parse API error response
 */
export function parseApiError(error: unknown): { message: string; details?: unknown } {
  if (error instanceof ApiError) {
    return {
      message: error.message,
      details: error.details
    }
  }

  if (error instanceof Error) {
    return {
      message: error.message
    }
  }

  if (typeof error === 'string') {
    return {
      message: error
    }
  }

  return {
    message: 'An unknown error occurred',
    details: error
  }
}

/**
 * Get user-friendly error message
 */
export function getUserFriendlyMessage(statusCode?: number): string {
  switch (statusCode) {
    case 400:
      return 'Invalid request. Please check your input.'
    case 401:
      return 'Unauthorized. Please log in again.'
    case 403:
      return 'Access denied. You do not have permission.'
    case 404:
      return 'Resource not found.'
    case 409:
      return 'Conflict. The resource already exists or has been modified.'
    case 422:
      return 'Invalid data. Please check your input.'
    case 429:
      return 'Too many requests. Please try again later.'
    case 500:
      return 'Server error. Please try again later.'
    case 502:
      return 'Service temporarily unavailable.'
    case 503:
      return 'Service unavailable. Please try again later.'
    default:
      return 'An error occurred. Please try again.'
  }
}

/**
 * Check if error is a network error
 */
export function isNetworkError(error: unknown): boolean {
  if (error instanceof Error) {
    return (
      error.message.includes('fetch') ||
      error.message.includes('network') ||
      error.message.includes('timeout') ||
      error.message.includes('ERR_INTERNET_DISCONNECTED')
    )
  }
  return false
}

/**
 * Check if error is a 4xx client error
 */
export function isClientError(statusCode?: number): boolean {
  return statusCode ? statusCode >= 400 && statusCode < 500 : false
}

/**
 * Check if error is a 5xx server error
 */
export function isServerError(statusCode?: number): boolean {
  return statusCode ? statusCode >= 500 : false
}

/**
 * Retry configuration for exponential backoff
 */
export interface RetryConfig {
  maxRetries: number
  delayMs: number
  backoffMultiplier: number
}

/**
 * Default retry configuration
 */
export const DEFAULT_RETRY_CONFIG: RetryConfig = {
  maxRetries: 3,
  delayMs: 1000,
  backoffMultiplier: 2
}

/**
 * Execute function with retry logic
 */
export async function withRetry<T>(
  fn: () => Promise<T>,
  config: RetryConfig = DEFAULT_RETRY_CONFIG
): Promise<T> {
  let lastError: Error | null = null

  for (let attempt = 0; attempt <= config.maxRetries; attempt++) {
    try {
      return await fn()
    } catch (error) {
      lastError = error instanceof Error ? error : new Error(String(error))

      // Don't retry on client errors (4xx)
      if (error instanceof ApiError && isClientError(error.statusCode)) {
        throw error
      }

      // Wait before retry (unless it's the last attempt)
      if (attempt < config.maxRetries) {
        const delayMs = config.delayMs * Math.pow(config.backoffMultiplier, attempt)
        await new Promise(resolve => setTimeout(resolve, delayMs))
      }
    }
  }

  throw lastError || new Error('Request failed after retries')
}

/**
 * Handle API response with standardized error checking
 */
export async function handleApiResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let errorData: unknown

    try {
      const contentType = response.headers.get('content-type')
      if (contentType?.includes('application/json')) {
        errorData = await response.json()
      }
    } catch {
      // Ignore parsing errors
    }

    throw new ApiError(
      response.status,
      getUserFriendlyMessage(response.status),
      errorData
    )
  }

  try {
    const contentType = response.headers.get('content-type')
    if (contentType?.includes('application/json')) {
      return await response.json()
    }
    return {} as T
  } catch (error) {
    throw new ApiError(response.status, 'Failed to parse response', error)
  }
}
