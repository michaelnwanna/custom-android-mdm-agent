<template>
  <div class="login-container">
    <div class="login-card">
      <div class="login-header">
        <h1>Management Console</h1>
        <p class="subtitle">MDM Fleet Management</p>
      </div>

      <form @submit.prevent="handleLogin" class="login-form">
        <!-- Email input -->
        <div class="form-group">
          <label for="email">Email Address</label>
          <input
            id="email"
            v-model="credentials.email"
            type="email"
            placeholder="admin@example.com"
            required
            :disabled="loading"
            class="form-input"
          />
        </div>

        <!-- Password input -->
        <div class="form-group">
          <label for="password">Password</label>
          <input
            id="password"
            v-model="credentials.password"
            type="password"
            placeholder="Enter your password"
            required
            :disabled="loading"
            class="form-input"
          />
        </div>

        <!-- Error message -->
        <div v-if="errorMessage" class="error-message">
          <svg class="error-icon" viewBox="0 0 20 20" fill="currentColor">
            <path
              fill-rule="evenodd"
              d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
              clip-rule="evenodd"
            />
          </svg>
          {{ errorMessage }}
        </div>

        <!-- Submit button -->
        <button type="submit" class="submit-btn" :disabled="loading">
          <span v-if="!loading">Sign In</span>
          <span v-else class="loading-spinner">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10" opacity="0.25" />
              <path d="M12 2a10 10 0 0 1 10 10" stroke-dasharray="15.7" stroke-dashoffset="0" />
            </svg>
            Signing in...
          </span>
        </button>
      </form>

      <!-- Development info -->
      <div v-if="isDevelopment" class="dev-info">
        <details>
          <summary>Development Info</summary>
          <p>
            For demo purposes, use any email and password: <code>password</code>
          </p>
          <p>
            Example: <code>admin@example.com</code> / <code>password</code>
          </p>
        </details>
      </div>

      <!-- Footer -->
      <div class="login-footer">
        <p class="footer-text">
          Custom Android MDM · <a href="https://github.com" target="_blank" rel="noopener">GitHub</a>
        </p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'
import type { LoginRequest } from '@/types'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

const credentials = reactive<LoginRequest>({
  email: '',
  password: ''
})

const loading = ref(false)
const errorMessage = ref<string | null>(null)

const isDevelopment = computed(() => import.meta.env.DEV)

/**
 * Handle login form submission
 */
async function handleLogin(): Promise<void> {
  // Validate inputs
  if (!credentials.email || !credentials.password) {
    errorMessage.value = 'Please enter both email and password'
    return
  }

  // Validate email format
  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
  if (!emailRegex.test(credentials.email)) {
    errorMessage.value = 'Please enter a valid email address'
    return
  }

  loading.value = true
  errorMessage.value = null

  try {
    const success = await authStore.login(credentials)

    if (success) {
      // Get redirect target from query params or default to dashboard
      const redirectTo = (route.query.redirect as string) || '/dashboard'
      router.push(redirectTo)
    } else {
      errorMessage.value = authStore.error || 'Login failed. Please try again.'
    }
  } catch (error) {
    errorMessage.value = 'An unexpected error occurred. Please try again.'
    console.error('Login error:', error)
  } finally {
    loading.value = false
  }
}

/**
 * Redirect to dashboard if already authenticated
 */
onMounted(() => {
  if (authStore.isAuthenticated) {
    router.push('/dashboard')
  }
})
</script>

<style scoped lang="scss">
.login-container {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  padding: 1rem;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

.login-card {
  width: 100%;
  max-width: 420px;
  background: white;
  border-radius: 12px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
  padding: 3rem 2rem;
}

.login-header {
  text-align: center;
  margin-bottom: 2rem;

  h1 {
    margin: 0;
    font-size: 1.75rem;
    font-weight: 600;
    color: #1a1a1a;
  }

  .subtitle {
    margin: 0.5rem 0 0 0;
    font-size: 0.875rem;
    color: #666;
  }
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;

  label {
    font-size: 0.875rem;
    font-weight: 500;
    color: #1a1a1a;
  }

  .form-input {
    padding: 0.75rem 1rem;
    border: 1px solid #ddd;
    border-radius: 6px;
    font-size: 1rem;
    transition: border-color 0.2s, box-shadow 0.2s;

    &:focus {
      outline: none;
      border-color: #667eea;
      box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.1);
    }

    &:disabled {
      background-color: #f5f5f5;
      cursor: not-allowed;
    }
  }
}

.error-message {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background-color: #fee;
  border: 1px solid #fcc;
  border-radius: 6px;
  font-size: 0.875rem;
  color: #c33;

  .error-icon {
    width: 1.25rem;
    height: 1.25rem;
    flex-shrink: 0;
  }
}

.submit-btn {
  padding: 0.875rem 1rem;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border: none;
  border-radius: 6px;
  font-size: 1rem;
  font-weight: 500;
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s;

  &:hover:not(:disabled) {
    transform: translateY(-2px);
    box-shadow: 0 10px 20px rgba(102, 126, 234, 0.3);
  }

  &:active:not(:disabled) {
    transform: translateY(0);
  }

  &:disabled {
    opacity: 0.8;
    cursor: not-allowed;
  }

  .loading-spinner {
    display: flex;
    align-items: center;
    gap: 0.5rem;

    svg {
      width: 1rem;
      height: 1rem;
      animation: spin 1s linear infinite;
    }
  }
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

.dev-info {
  margin-top: 1.5rem;
  padding: 1rem;
  background-color: #f0f4ff;
  border: 1px solid #d4e1ff;
  border-radius: 6px;
  font-size: 0.875rem;
  color: #444;

  details {
    cursor: pointer;

    summary {
      font-weight: 500;
      color: #667eea;
      user-select: none;
    }

    p {
      margin: 0.5rem 0 0 1rem;
    }

    code {
      background-color: white;
      padding: 0.2rem 0.4rem;
      border-radius: 3px;
      font-family: 'Courier New', monospace;
      font-size: 0.8rem;
      color: #c33;
    }
  }
}

.login-footer {
  text-align: center;
  margin-top: 2rem;
  padding-top: 1.5rem;
  border-top: 1px solid #eee;

  .footer-text {
    margin: 0;
    font-size: 0.75rem;
    color: #999;

    a {
      color: #667eea;
      text-decoration: none;

      &:hover {
        text-decoration: underline;
      }
    }
  }
}

@media (max-width: 480px) {
  .login-card {
    padding: 2rem 1.5rem;
  }

  .login-header h1 {
    font-size: 1.5rem;
  }

  .login-form {
    gap: 1rem;
  }
}
</style>
