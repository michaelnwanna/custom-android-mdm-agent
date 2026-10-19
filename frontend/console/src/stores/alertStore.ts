import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import apiClient from '@services/apiClient'
import type { HealthAlert } from '@types'

export const useAlertStore = defineStore('alert', () => {
  const alerts = ref<HealthAlert[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  const activeAlerts = computed(() => 
    alerts.value.filter(a => !a.resolvedAt)
  )

  const criticalAlerts = computed(() =>
    activeAlerts.value.filter(a => a.severity === 'Critical')
  )

  const alertsByType = computed(() => {
    const grouped: Record<string, HealthAlert[]> = {}
    activeAlerts.value.forEach(alert => {
      if (!grouped[alert.alertType]) {
        grouped[alert.alertType] = []
      }
      grouped[alert.alertType].push(alert)
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
      error.value = err instanceof Error ? err.message : 'Failed to fetch alerts'
    } finally {
      loading.value = false
    }
  }

  async function fetchDeviceAlerts(deviceId: string) {
    try {
      const response = await apiClient.get<HealthAlert[]>(`/devices/${deviceId}/health/alerts`)
      return response.data
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch device alerts'
      return []
    }
  }

  async function resolveAlert(alertId: string) {
    try {
      await apiClient.post(`/health/alerts/${alertId}/resolve`, {})
      const alert = alerts.value.find(a => a.alertId === alertId)
      if (alert) {
        alert.resolvedAt = new Date().toISOString()
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to resolve alert'
    }
  }

  function addAlert(alert: HealthAlert) {
    const existing = alerts.value.find(a => a.alertId === alert.alertId)
    if (!existing) {
      alerts.value.push(alert)
    }
  }

  function removeAlert(alertId: string) {
    const index = alerts.value.findIndex(a => a.alertId === alertId)
    if (index > -1) {
      alerts.value.splice(index, 1)
    }
  }

  function clearError() {
    error.value = null
  }

  return {
    alerts,
    loading,
    error,
    activeAlerts,
    criticalAlerts,
    alertsByType,
    fetchAlerts,
    fetchDeviceAlerts,
    resolveAlert,
    addAlert,
    removeAlert,
    clearError
  }
})
