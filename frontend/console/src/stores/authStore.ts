import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { AuthUser, LoginRequest, LoginResponse } from '@/types'
import { apiClient } from '@/api/client'
import { API_ENDPOINTS } from '@/api/endpoints'
import { parseApiError } from '@/api/handlers'

const TOKEN_KEY = 'auth_token'
const REFRESH_TOKEN_KEY = 'refresh_token'
const USER_KEY = 'auth_user'
const TOKEN_EXPIRY_KEY = 'auth_token_expiry'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(TOKEN_KEY))
  const refreshToken = ref<string | null>(localStorage.getItem(REFRESH_TOKEN_KEY))
  const storedUser = localStorage.getItem(USER_KEY)
  const user = ref<AuthUser | null>(storedUser ? JSON.parse(storedUser) as AuthUser : null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // Computed properties
  const isAuthenticated = computed(() => !!token.value && !!user.value)
  const isTokenExpired = computed(() => {
    if (!token.value) return true
    const expiry = localStorage.getItem(TOKEN_EXPIRY_KEY)
    if (!expiry) return false
    return Date.now() > parseInt(expiry)
  })

  /**
   * Set token and calculate expiry
   */
  function setToken(newToken: string, expiresIn?: number): void {
    token.value = newToken
    apiClient.setToken(newToken)
    localStorage.setItem(TOKEN_KEY, newToken)

    // Calculate and store expiry time
    if (expiresIn) {
      const expiryTime = Date.now() + expiresIn * 1000
      localStorage.setItem(TOKEN_EXPIRY_KEY, expiryTime.toString())
    }
  }

  /**
   * Set refresh token
   */
  function setRefreshToken(newRefreshToken: string): void {
    refreshToken.value = newRefreshToken
    apiClient.setRefreshToken(newRefreshToken)
    localStorage.setItem(REFRESH_TOKEN_KEY, newRefreshToken)
  }

  /**
   * Set user info
   */
  function setUser(newUser: AuthUser): void {
    user.value = newUser
    localStorage.setItem(USER_KEY, JSON.stringify(newUser))
  }

  /**
   * Clear all auth data
   */
  function clearAuth(): void {
    token.value = null
    refreshToken.value = null
    user.value = null
    error.value = null
    apiClient.clearTokens()
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(REFRESH_TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    localStorage.removeItem(TOKEN_EXPIRY_KEY)
  }

  /**
   * Login with email and password
   */
  async function login(credentials: LoginRequest): Promise<boolean> {
    loading.value = true
    error.value = null

    try {
      // For development: try real API first, fall back to mock login
      let response: LoginResponse | null = null

      try {
        response = await apiClient.post<LoginResponse>(API_ENDPOINTS.AUTH.LOGIN, credentials)
      } catch (apiError) {
        console.log('API login failed, using mock authentication:', parseApiError(apiError))

        // Mock login - accept any non-empty email and password "password"
        if (credentials.email && credentials.password === 'password') {
          response = {
            token: {
              accessToken: 'mock_jwt_token_' + Date.now(),
              refreshToken: 'mock_refresh_token_' + Date.now(),
              expiresIn: 3600,
              tokenType: 'Bearer'
            },
            user: {
              id: 'user_' + Date.now(),
              email: credentials.email,
              name: credentials.email.split('@')[0],
              role: 'Admin',
              organizationId: 'org_default'
            }
          }
        }
      }

      if (!response) {
        throw new Error('Invalid email or password')
      }

      // Store auth data
      const { token: authToken, user: authUser } = response
      setToken(authToken.accessToken, authToken.expiresIn)
      setRefreshToken(authToken.refreshToken)
      setUser(authUser)

      loading.value = false
      return true
    } catch (err) {
      const { message } = parseApiError(err)
      error.value = message
      loading.value = false
      return false
    }
  }

  /**
   * Refresh access token
   */
  async function refresh(): Promise<boolean> {
    if (!refreshToken.value) {
      clearAuth()
      return false
    }

    try {
      const response = await apiClient.post<LoginResponse>(API_ENDPOINTS.AUTH.REFRESH, {
        refreshToken: refreshToken.value
      })

      const { token: authToken, user: authUser } = response
      setToken(authToken.accessToken, authToken.expiresIn)
      setRefreshToken(authToken.refreshToken)
      setUser(authUser)

      return true
    } catch (err) {
      const { message } = parseApiError(err)
      error.value = message
      clearAuth()
      return false
    }
  }

  /**
   * Logout
   */
  async function logout(): Promise<void> {
    try {
      // Try to notify server but don't fail if it does
      if (token.value) {
        await apiClient.post(API_ENDPOINTS.AUTH.LOGOUT, {})
      }
    } catch {
      // Ignore logout API errors
    } finally {
      clearAuth()
    }
  }

  /**
   * Verify token is still valid
   */
  async function verify(): Promise<boolean> {
    if (!token.value) {
      return false
    }

    try {
      const response = await apiClient.post<{ valid: boolean }>(
        API_ENDPOINTS.AUTH.VERIFY,
        {}
      )
      return response.valid ?? true
    } catch (err) {
      console.error('Token verification failed:', err)
      // If verification fails, try to refresh
      if (refreshToken.value) {
        return refresh()
      }
      clearAuth()
      return false
    }
  }

  /**
   * Initialize auth from storage (call on app startup)
   */
  function initialize(): void {
    const storedToken = localStorage.getItem(TOKEN_KEY)
    const storedUser = localStorage.getItem(USER_KEY)

    if (storedToken && storedUser) {
      token.value = storedToken
      user.value = JSON.parse(storedUser)
      apiClient.setToken(storedToken)

      const storedRefreshToken = localStorage.getItem(REFRESH_TOKEN_KEY)
      if (storedRefreshToken) {
        refreshToken.value = storedRefreshToken
        apiClient.setRefreshToken(storedRefreshToken)
      }
    }
  }

  return {
    // State
    token,
    user,
    loading,
    error,

    // Computed
    isAuthenticated,
    isTokenExpired,

    // Methods
    login,
    logout,
    refresh,
    verify,
    initialize,
    clearAuth,
    setToken,
    setUser
  }
})
