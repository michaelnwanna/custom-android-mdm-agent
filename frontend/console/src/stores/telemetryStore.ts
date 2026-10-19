import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import apiClient from '@services/apiClient'
import type { DeviceTelemetry, FleetHealthMetrics } from '@types'

export const useTelemetryStore = defineStore('telemetry', () => {
  const fleetMetrics = ref<FleetHealthMetrics | null>(null)
  const deviceTelemetry = ref<Map<string, DeviceTelemetry>>(new Map())
  const loading = ref(false)
  const error = ref<string | null>(null)
  const lastUpdated = ref<Date | null>(null)

  const healthScore = computed(() => {
    if (!fleetMetrics.value) return 0
    const total = fleetMetrics.value.totalDevices
    if (total === 0) return 100
    
    const healthy = total - 
      (fleetMetrics.value.offlineDevices || 0) - 
      (fleetMetrics.value.lowBatteryDevices || 0) - 
      (fleetMetrics.value.lowStorageDevices || 0)
    
    return Math.round((healthy / total) * 100)
  })

  async function fetchFleetMetrics() {
    loading.value = true
    error.value = null
    
    try {
      const response = await apiClient.get<FleetHealthMetrics>('/fleet/health/metrics')
      fleetMetrics.value = response.data
      lastUpdated.value = new Date()
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch fleet metrics'
    } finally {
      loading.value = false
    }
  }

  async function fetchDeviceTelemetry(deviceId: string) {
    try {
      const response = await apiClient.get<DeviceTelemetry>(
        `/devices/${deviceId}/telemetry/latest`
      )
      deviceTelemetry.value.set(deviceId, response.data)
      return response.data
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch device telemetry'
    }
  }

  function updateDeviceTelemetry(deviceId: string, telemetry: DeviceTelemetry) {
    deviceTelemetry.value.set(deviceId, telemetry)
    lastUpdated.value = new Date()
  }

  function updateFleetMetrics(metrics: Partial<FleetHealthMetrics>) {
    if (fleetMetrics.value) {
      fleetMetrics.value = { ...fleetMetrics.value, ...metrics }
      lastUpdated.value = new Date()
    }
  }

  function getDeviceTelemetry(deviceId: string) {
    return deviceTelemetry.value.get(deviceId)
  }

  function clearError() {
    error.value = null
  }

  return {
    fleetMetrics,
    deviceTelemetry,
    loading,
    error,
    lastUpdated,
    healthScore,
    fetchFleetMetrics,
    fetchDeviceTelemetry,
    updateDeviceTelemetry,
    updateFleetMetrics,
    getDeviceTelemetry,
    clearError
  }
})
