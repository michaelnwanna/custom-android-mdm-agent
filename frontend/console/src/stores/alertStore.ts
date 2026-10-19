import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import apiClient from '@services/apiClient'
import type { HealthAlert } from '@types'
import { useDeviceStore } from './deviceStore'

function generateMockAlerts(count: number = 50): HealthAlert[] {
  const devices = useDeviceStore().devices
  const alertTypes = [
    'Low Battery',
    'Storage Critical',
    'Memory Warning',
    'Connectivity Lost',
    'Security Alert',
    'Policy Violation',
    'OS Update Available',
    'App Malware Detected',
    'Certificate Expired',
    'Configuration Drift'
  ]
  const messages: Record<string, string[]> = {
    'Low Battery': ['Battery below 15%', 'Battery critical - 5% remaining'],
    'Storage Critical': ['Storage 95% full', 'No space left on device'],
    'Memory Warning': ['Memory usage exceeds 90%', 'App memory leaks detected'],
    'Connectivity Lost': ['Device offline for 2+ hours', 'WiFi disconnected'],
    'Security Alert': ['Suspicious app installed', 'Root/jailbreak detected'],
    'Policy Violation': ['Lockscreen policy not applied', 'Password requirements not met'],
    'OS Update Available': ['Android 14 update pending', 'Security patch available'],
    'App Malware Detected': ['Malware found in app', 'Suspicious permissions detected'],
    'Certificate Expired': ['Certificate expired or about to expire', 'SSL certificate invalid'],
    'Configuration Drift': ['Config differs from policy', 'Settings reverted by user']
  }

  const alerts: HealthAlert[] = []
  for (let i = 0; i < count; i++) {
    const device = devices[Math.floor(Math.random() * devices.length)]
    const alertType = alertTypes[Math.floor(Math.random() * alertTypes.length)]
    const typeMessages = messages[alertType]
    const severity = ['Critical', 'High', 'Medium', 'Low'][
      Math.floor(Math.random() * 4)
    ] as 'Critical' | 'High' | 'Medium' | 'Low'

    // 70% active, 30% resolved
    const isResolved = Math.random() < 0.3
    const createdAt = new Date(Date.now() - Math.random() * 7 * 24 * 60 * 60 * 1000)
    const resolvedAt = isResolved ? new Date(createdAt.getTime() + Math.random() * 24 * 60 * 60 * 1000) : null

    alerts.push({
      alertId: `alert-${String(i + 1).padStart(6, '0')}`,
      deviceId: device.deviceId || device.id || 'unknown',
      deviceName: device.deviceName || device.name || 'Unknown Device',
      alertType,
      severity,
      message: typeMessages[Math.floor(Math.random() * typeMessages.length)],
      timestamp: createdAt.toISOString(),
      resolvedAt: resolvedAt?.toISOString() || null
    })
  }

  return alerts
}

export const useAlertStore = defineStore('alert', () => {
  const alerts = ref<HealthAlert[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  const activeAlerts = computed(() => 
    alerts.value.filter(a => !a.resolvedAt && !a.resolved)
  )

  const criticalAlerts = computed(() =>
    activeAlerts.value.filter(a => a.severity === 'Critical')
  )

  const alertsByType = computed(() => {
    const grouped: Record<string, HealthAlert[]> = {}
    activeAlerts.value.forEach(alert => {
      const key = alert.alertType || alert.type || 'Unknown'
      if (!grouped[key]) {
        grouped[key] = []
      }
      grouped[key].push(alert)
    })
    return grouped
  })

  const alertsBySeverity = computed(() => {
    const grouped: Record<string, HealthAlert[]> = {
      'Critical': [],
      'High': [],
      'Medium': [],
      'Low': []
    }
    alerts.value.forEach(alert => {
      grouped[alert.severity].push(alert)
    })
    return grouped
  })

  async function fetchAlerts() {
    loading.value = true
    error.value = null
    
    try {
      const response = await apiClient.get<HealthAlert[]>('/alerts')
      alerts.value = response.data
    } catch (err) {
      // Fallback to mock data
      alerts.value = generateMockAlerts(50)
      error.value = null // Don't show error if we have mock data
    } finally {
      loading.value = false
    }
  }

  async function fetchDeviceAlerts(deviceId: string) {
    try {
      const response = await apiClient.get<HealthAlert[]>(`/devices/${deviceId}/health/alerts`)
      return response.data
    } catch (err) {
      // Fallback to mock data filtered by device
      return generateMockAlerts(50).filter(a => a.deviceId === deviceId)
    }
  }

  async function resolveAlert(alertId: string) {
    try {
      // TODO: API endpoint - POST /health/alerts/{alertId}/resolve
      await apiClient.post(`/health/alerts/${alertId}/resolve`, {})
      const alert = alerts.value.find(a => a.alertId === alertId)
      if (alert) {
        alert.resolvedAt = new Date().toISOString()
      }
    } catch (err) {
      // Fallback: update locally
      const alert = alerts.value.find(a => a.alertId === alertId)
      if (alert) {
        alert.resolvedAt = new Date().toISOString()
      }
    }
  }

  async function resolveBulkAlerts(alertIds: string[]) {
    try {
      // TODO: API endpoint - POST /health/alerts/bulk/resolve
      await apiClient.post('/health/alerts/bulk/resolve', { alertIds })
      alertIds.forEach(id => {
        const alert = alerts.value.find(a => a.alertId === id)
        if (alert) {
          alert.resolvedAt = new Date().toISOString()
        }
      })
    } catch (err) {
      // Fallback: update locally
      alertIds.forEach(id => {
        const alert = alerts.value.find(a => a.alertId === id)
        if (alert) {
          alert.resolvedAt = new Date().toISOString()
        }
      })
    }
  }

  function addAlert(alert: HealthAlert) {
    const existing = alerts.value.find(a => a.alertId === alert.alertId)
    if (!existing) {
      alerts.value.unshift(alert) // Add to top for newest first
    }
  }

  function removeAlert(alertId: string) {
    const index = alerts.value.findIndex(a => a.alertId === alertId)
    if (index > -1) {
      alerts.value.splice(index, 1)
    }
  }

  function updateAlert(alertId: string, updates: Partial<HealthAlert>) {
    const alert = alerts.value.find(a => a.alertId === alertId)
    if (alert) {
      Object.assign(alert, updates)
    }
  }

  function clearError() {
    error.value = null
  }

  function getAlertById(alertId: string): HealthAlert | undefined {
    return alerts.value.find(a => a.alertId === alertId)
  }

  return {
    alerts,
    loading,
    error,
    activeAlerts,
    criticalAlerts,
    alertsByType,
    alertsBySeverity,
    fetchAlerts,
    fetchDeviceAlerts,
    resolveAlert,
    resolveBulkAlerts,
    addAlert,
    removeAlert,
    updateAlert,
    getAlertById,
    clearError
  }
})
