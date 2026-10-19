import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as alertApi from '@/api/alertApi'
import type { HealthAlert } from '@types'

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
      const result = await alertApi.fetchAlerts({ page: 1, pageSize: 100 })
      alerts.value = result.items
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch alerts'
      console.error('Fetch alerts error:', err)
    } finally {
      loading.value = false
    }
  }

  async function fetchDeviceAlerts(deviceId: string) {
    try {
      return await alertApi.getDeviceAlerts(deviceId)
    } catch (err) {
      console.error(`Fetch device alerts error for ${deviceId}:`, err)
      return []
    }
  }

  async function resolveAlert(alertId: string) {
    try {
      await alertApi.resolveAlert(alertId)
      const alert = alerts.value.find(a => a.alertId === alertId)
      if (alert) {
        alert.resolvedAt = new Date().toISOString()
        alert.resolved = true
      }
    } catch (err) {
      console.error('Resolve alert error:', err)
      throw err
    }
  }

  async function resolveBulkAlerts(alertIds: string[]) {
    try {
      await alertApi.bulkResolveAlerts(alertIds)
      alertIds.forEach(id => {
        const alert = alerts.value.find(a => a.alertId === id)
        if (alert) {
          alert.resolvedAt = new Date().toISOString()
          alert.resolved = true
        }
      })
    } catch (err) {
      console.error('Bulk resolve alerts error:', err)
      throw err
    }
  }

  async function deleteAlert(alertId: string) {
    try {
      await alertApi.deleteAlert(alertId)
      const index = alerts.value.findIndex(a => a.alertId === alertId)
      if (index > -1) {
        alerts.value.splice(index, 1)
      }
    } catch (err) {
      console.error('Delete alert error:', err)
      throw err
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
    deleteAlert,
    addAlert,
    removeAlert,
    updateAlert,
    getAlertById,
    clearError
  }
})
