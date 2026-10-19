import type { AuthError } from '@/types'

export class ApiClient {
  private defaultHeaders: Record<string, string>

  constructor() {
    this.defaultHeaders = {
      'Content-Type': 'application/json'
    }
  }

  /**
   * Get authorization header from localStorage
   */
  private getAuthHeader(): string {
    const token = localStorage.getItem('auth_token')
    return token ? `Bearer ${token}` : ''
  }

  /**
   * Get request headers with auth token
   */
  private getHeaders(headers?: Record<string, string>): Record<string, string> {
    const authHeader = this.getAuthHeader()
    return {
      ...this.defaultHeaders,
      ...(authHeader && { Authorization: authHeader }),
      ...headers
    }
  }

  /**
   * Handle response and check for errors
   */
  private async handleResponse<T>(response: Response): Promise<T> {
    // Check for 401 Unauthorized
    if (response.status === 401) {
      // Clear stored tokens
      localStorage.removeItem('auth_token')
      localStorage.removeItem('refresh_token')
      // Redirect to login
      window.location.href = '/login'
      throw new Error('Unauthorized')
    }

    // Check for other HTTP errors
    if (!response.ok) {
      const contentType = response.headers.get('content-type')
      let errorData: AuthError | string = `HTTP ${response.status}`

      if (contentType?.includes('application/json')) {
        try {
          const error = await response.json()
          errorData = error.message || error.code || errorData
        } catch {
          // Ignore JSON parse errors
        }
      }

      const error = new Error(typeof errorData === 'string' ? errorData : JSON.stringify(errorData))
      throw error
    }

    // Parse response
    const contentType = response.headers.get('content-type')
    if (contentType?.includes('application/json')) {
      return await response.json()
    }

    return {} as T
  }

  /**
   * GET request
   */
  async get<T = unknown>(
    url: string,
    options?: {
      headers?: Record<string, string>
      params?: Record<string, string | number | boolean>
    }
  ): Promise<T> {
    try {
      // Build URL with query parameters
      let finalUrl = url
      if (options?.params) {
        const searchParams = new URLSearchParams()
        Object.entries(options.params).forEach(([key, value]) => {
          searchParams.append(key, String(value))
        })
        finalUrl = `${url}?${searchParams.toString()}`
      }

      const response = await fetch(finalUrl, {
        method: 'GET',
        headers: this.getHeaders(options?.headers)
      })

      return await this.handleResponse<T>(response)
    } catch (error) {
      console.error('GET request failed:', url, error)
      throw error
    }
  }

  /**
   * POST request
   */
  async post<T = unknown>(
    url: string,
    data?: unknown,
    options?: {
      headers?: Record<string, string>
    }
  ): Promise<T> {
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: this.getHeaders(options?.headers),
        body: data ? JSON.stringify(data) : undefined
      })

      return await this.handleResponse<T>(response)
    } catch (error) {
      console.error('POST request failed:', url, error)
      throw error
    }
  }

  /**
   * PUT request
   */
  async put<T = unknown>(
    url: string,
    data?: unknown,
    options?: {
      headers?: Record<string, string>
    }
  ): Promise<T> {
    try {
      const response = await fetch(url, {
        method: 'PUT',
        headers: this.getHeaders(options?.headers),
        body: data ? JSON.stringify(data) : undefined
      })

      return await this.handleResponse<T>(response)
    } catch (error) {
      console.error('PUT request failed:', url, error)
      throw error
    }
  }

  /**
   * PATCH request
   */
  async patch<T = unknown>(
    url: string,
    data?: unknown,
    options?: {
      headers?: Record<string, string>
    }
  ): Promise<T> {
    try {
      const response = await fetch(url, {
        method: 'PATCH',
        headers: this.getHeaders(options?.headers),
        body: data ? JSON.stringify(data) : undefined
      })

      return await this.handleResponse<T>(response)
    } catch (error) {
      console.error('PATCH request failed:', url, error)
      throw error
    }
  }

  /**
   * DELETE request
   */
  async delete<T = unknown>(
    url: string,
    options?: {
      headers?: Record<string, string>
    }
  ): Promise<T> {
    try {
      const response = await fetch(url, {
        method: 'DELETE',
        headers: this.getHeaders(options?.headers)
      })

      return await this.handleResponse<T>(response)
    } catch (error) {
      console.error('DELETE request failed:', url, error)
      throw error
    }
  }

  /**
   * Set authorization token
   */
  setToken(token: string): void {
    localStorage.setItem('auth_token', token)
  }

  /**
   * Set refresh token
   */
  setRefreshToken(token: string): void {
    localStorage.setItem('refresh_token', token)
  }

  /**
   * Clear authorization tokens
   */
  clearTokens(): void {
    localStorage.removeItem('auth_token')
    localStorage.removeItem('refresh_token')
  }

  /**
   * Get current token
   */
  getToken(): string | null {
    return localStorage.getItem('auth_token')
  }
}

// Create and export default client instance
export const apiClient = new ApiClient()

export default apiClient
