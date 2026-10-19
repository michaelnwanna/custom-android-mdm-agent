import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export interface SettingCategory {
  id: string
  name: string
  description: string
  icon: string
}

export interface Setting {
  id: string
  key: string
  name: string
  description: string
  type: 'string' | 'number' | 'boolean' | 'select'
  value: string | number | boolean
  category: string
  options?: Array<{ label: string; value: string | number }>
  required?: boolean
  validation?: {
    minLength?: number
    maxLength?: number
    min?: number
    max?: number
    pattern?: string
  }
}

export const useSettingsStore = defineStore('settings', () => {
  const settings = ref<Setting[]>([])
  const categories = ref<SettingCategory[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // Define setting categories
  function initializeCategories() {
    categories.value = [
      {
        id: 'general',
        name: 'General',
        description: 'General system settings',
        icon: '⚙️'
      },
      {
        id: 'security',
        name: 'Security',
        description: 'Security policies and authentication',
        icon: '🔒'
      },
      {
        id: 'notifications',
        name: 'Notifications',
        description: 'Alert and notification settings',
        icon: '🔔'
      },
      {
        id: 'deployment',
        name: 'Deployment',
        description: 'Policy and release deployment settings',
        icon: '🚀'
      },
      {
        id: 'monitoring',
        name: 'Monitoring',
        description: 'Device monitoring and telemetry',
        icon: '📊'
      },
      {
        id: 'advanced',
        name: 'Advanced',
        description: 'Advanced system configuration',
        icon: '🔧'
      }
    ]
  }

  function generateMockSettings(): Setting[] {
    return [
      // General Settings
      {
        id: 'org-name',
        key: 'ORGANIZATION_NAME',
        name: 'Organization Name',
        description: 'Your organization name displayed in the console',
        type: 'string',
        value: 'Acme Corporation',
        category: 'general',
        required: true
      },
      {
        id: 'org-logo-url',
        key: 'ORGANIZATION_LOGO_URL',
        name: 'Organization Logo URL',
        description: 'URL to your organization logo for branding',
        type: 'string',
        value: 'https://example.com/logo.png',
        category: 'general'
      },
      {
        id: 'console-language',
        key: 'CONSOLE_LANGUAGE',
        name: 'Console Language',
        description: 'Default language for the management console',
        type: 'select',
        value: 'en-US',
        category: 'general',
        options: [
          { label: 'English (US)', value: 'en-US' },
          { label: 'English (UK)', value: 'en-GB' },
          { label: 'Spanish', value: 'es-ES' },
          { label: 'French', value: 'fr-FR' },
          { label: 'German', value: 'de-DE' },
          { label: 'Chinese', value: 'zh-CN' }
        ]
      },
      {
        id: 'time-zone',
        key: 'TIME_ZONE',
        name: 'Time Zone',
        description: 'Default time zone for displaying timestamps',
        type: 'select',
        value: 'UTC',
        category: 'general',
        options: [
          { label: 'UTC', value: 'UTC' },
          { label: 'EST', value: 'US/Eastern' },
          { label: 'CST', value: 'US/Central' },
          { label: 'PST', value: 'US/Pacific' },
          { label: 'IST', value: 'Asia/Kolkata' },
          { label: 'GMT+8', value: 'Asia/Shanghai' }
        ]
      },

      // Security Settings
      {
        id: 'require-mfa',
        key: 'REQUIRE_MFA',
        name: 'Require Multi-Factor Authentication',
        description: 'Enforce MFA for all console users',
        type: 'boolean',
        value: true,
        category: 'security'
      },
      {
        id: 'session-timeout',
        key: 'SESSION_TIMEOUT_MINUTES',
        name: 'Session Timeout (minutes)',
        description: 'Automatically log out inactive users after this duration',
        type: 'number',
        value: 30,
        category: 'security',
        validation: { min: 5, max: 480 }
      },
      {
        id: 'pwd-expiry',
        key: 'PASSWORD_EXPIRY_DAYS',
        name: 'Password Expiry (days)',
        description: 'Force password change after this many days (0 = disabled)',
        type: 'number',
        value: 90,
        category: 'security',
        validation: { min: 0, max: 365 }
      },
      {
        id: 'tls-required',
        key: 'REQUIRE_TLS',
        name: 'Require TLS for All Connections',
        description: 'Enforce encrypted connections for all devices and APIs',
        type: 'boolean',
        value: true,
        category: 'security'
      },

      // Notification Settings
      {
        id: 'alert-critical',
        key: 'ALERT_CRITICAL_ENABLED',
        name: 'Send Critical Alerts',
        description: 'Enable notifications for critical severity alerts',
        type: 'boolean',
        value: true,
        category: 'notifications'
      },
      {
        id: 'alert-high',
        key: 'ALERT_HIGH_ENABLED',
        name: 'Send High Priority Alerts',
        description: 'Enable notifications for high priority alerts',
        type: 'boolean',
        value: true,
        category: 'notifications'
      },
      {
        id: 'email-notifications',
        key: 'EMAIL_NOTIFICATIONS_ENABLED',
        name: 'Email Notifications',
        description: 'Send alerts via email to administrators',
        type: 'boolean',
        value: true,
        category: 'notifications'
      },
      {
        id: 'webhook-url',
        key: 'WEBHOOK_URL',
        name: 'Webhook URL for Events',
        description: 'Send important events to this URL (optional)',
        type: 'string',
        value: 'https://example.com/webhooks/mdm',
        category: 'notifications'
      },

      // Deployment Settings
      {
        id: 'auto-policy-deploy',
        key: 'AUTO_DEPLOY_POLICIES',
        name: 'Auto-Deploy Policies',
        description: 'Automatically deploy policies to enrolled devices',
        type: 'boolean',
        value: true,
        category: 'deployment'
      },
      {
        id: 'policy-retry',
        key: 'POLICY_RETRY_ATTEMPTS',
        name: 'Policy Retry Attempts',
        description: 'Retry policy deployment this many times before marking as failed',
        type: 'number',
        value: 3,
        category: 'deployment',
        validation: { min: 1, max: 10 }
      },
      {
        id: 'release-staging',
        key: 'RELEASE_STAGING_ENABLED',
        name: 'Release Staging',
        description: 'Allow staged rollout of releases to device groups',
        type: 'boolean',
        value: true,
        category: 'deployment'
      },

      // Monitoring Settings
      {
        id: 'telemetry-enabled',
        key: 'TELEMETRY_COLLECTION_ENABLED',
        name: 'Collect Device Telemetry',
        description: 'Collect device performance metrics (battery, storage, CPU)',
        type: 'boolean',
        value: true,
        category: 'monitoring'
      },
      {
        id: 'telemetry-interval',
        key: 'TELEMETRY_INTERVAL_MINUTES',
        name: 'Telemetry Collection Interval (minutes)',
        description: 'Collect telemetry from devices at this interval',
        type: 'number',
        value: 15,
        category: 'monitoring',
        validation: { min: 5, max: 1440 }
      },
      {
        id: 'alert-battery-threshold',
        key: 'ALERT_BATTERY_THRESHOLD',
        name: 'Low Battery Alert Threshold (%)',
        description: 'Alert when device battery falls below this percentage',
        type: 'number',
        value: 20,
        category: 'monitoring',
        validation: { min: 5, max: 50 }
      },
      {
        id: 'alert-storage-threshold',
        key: 'ALERT_STORAGE_THRESHOLD',
        name: 'Low Storage Alert Threshold (%)',
        description: 'Alert when device storage usage exceeds this percentage',
        type: 'number',
        value: 80,
        category: 'monitoring',
        validation: { min: 50, max: 95 }
      },

      // Advanced Settings
      {
        id: 'api-rate-limit',
        key: 'API_RATE_LIMIT_PER_MINUTE',
        name: 'API Rate Limit (requests/minute)',
        description: 'Maximum API requests allowed per minute per device',
        type: 'number',
        value: 100,
        category: 'advanced',
        validation: { min: 10, max: 1000 }
      },
      {
        id: 'debug-logging',
        key: 'DEBUG_LOGGING_ENABLED',
        name: 'Debug Logging',
        description: 'Enable verbose logging for troubleshooting',
        type: 'boolean',
        value: false,
        category: 'advanced'
      },
      {
        id: 'audit-trail',
        key: 'AUDIT_TRAIL_ENABLED',
        name: 'Audit Trail',
        description: 'Log all console and device actions for compliance',
        type: 'boolean',
        value: true,
        category: 'advanced'
      }
    ]
  }

  async function fetchSettings() {
    loading.value = true
    error.value = null
    initializeCategories()

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch('/api/settings')

      if (!response.ok) throw new Error('Failed to fetch settings')

      const data = await response.json()
      settings.value = data.items || data
    } catch (err) {
      // Fallback to mock data
      settings.value = generateMockSettings()
    } finally {
      loading.value = false
    }
  }

  async function updateSetting(settingId: string, value: string | number | boolean) {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch(`/api/settings/${settingId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ value })
      })

      if (!response.ok) throw new Error('Failed to update setting')

      const updatedSetting = await response.json()
      const index = settings.value.findIndex(s => s.id === settingId)
      if (index !== -1) {
        settings.value[index] = updatedSetting
      }
      return updatedSetting
    } catch (err) {
      // Update locally
      const index = settings.value.findIndex(s => s.id === settingId)
      if (index !== -1) {
        settings.value[index].value = value
        return settings.value[index]
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  async function resetSettings() {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch('/api/settings/reset', {
        method: 'POST'
      })

      if (!response.ok) throw new Error('Failed to reset settings')

      // Reset to defaults
      settings.value = generateMockSettings()
    } catch (err) {
      settings.value = generateMockSettings()
    } finally {
      loading.value = false
    }
  }

  // Computed properties
  const settingsByCategory = computed(() => {
    const grouped: Record<string, Setting[]> = {}
    settings.value.forEach(setting => {
      if (!grouped[setting.category]) {
        grouped[setting.category] = []
      }
      grouped[setting.category].push(setting)
    })
    return grouped
  })

  const categoriesWithSettings = computed(() => {
    return categories.value.map(cat => ({
      ...cat,
      settings: settingsByCategory.value[cat.id] || [],
      settingCount: (settingsByCategory.value[cat.id] || []).length
    }))
  })

  return {
    settings,
    categories,
    loading,
    error,
    fetchSettings,
    updateSetting,
    resetSettings,
    settingsByCategory,
    categoriesWithSettings
  }
})
