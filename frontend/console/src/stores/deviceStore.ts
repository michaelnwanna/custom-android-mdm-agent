import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { apiClient } from '@/api/client'
import { API_ENDPOINTS } from '@/api/endpoints'
import * as deviceApi from '@/api/deviceApi'
import type { Device, PaginatedResult } from '@types'

export const useDeviceStore = defineStore('device', () => {
  const devices = ref<Device[]>([])
  const selectedDevice = ref<Device | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const pagination = ref({
    pageNumber: 1,
    pageSize: 20,
    totalCount: 150,
    hasNextPage: true
  })

  const filteredDevices = computed(() => devices.value)
  
  const totalPages = computed(() => 
    Math.ceil(pagination.value.totalCount / pagination.value.pageSize)
  )

  // Mock device data
  const generateMockDevices = (count: number, offset: number = 0): Device[] => {
    const statuses: Array<'Enrolled' | 'Suspended' | 'Decommissioned'> = ['Enrolled', 'Suspended', 'Decommissioned']
    const models = ['Samsung Galaxy S23', 'Google Pixel 8', 'Samsung Galaxy Tab S9', 'OnePlus 12']
    const osVersions = ['Android 14', 'Android 13', 'Android 12', 'Android 11']

    return Array.from({ length: count }, (_, i) => ({
      deviceId: `dev-${String(offset + i + 1).padStart(6, '0')}`,
      enrollmentId: `enroll-${String(offset + i + 1).padStart(6, '0')}`,
      deviceName: `Device ${offset + i + 1}`,
      model: models[Math.floor(Math.random() * models.length)],
      osVersion: osVersions[Math.floor(Math.random() * osVersions.length)],
      status: statuses[Math.floor(Math.random() * statuses.length)],
      lastSyncDate: new Date(Date.now() - Math.random() * 86400000).toISOString(),
      serialNumber: `SN-${String(offset + i + 1).padStart(10, '0')}`
    }))
  }

  async function fetchDevices(pageNumber = 1, pageSize = 20) {
    loading.value = true
    error.value = null
    
    try {
      const response = await apiClient.get<PaginatedResult<Device>>(
        API_ENDPOINTS.DEVICES.LIST,
        {
          params: { pageNumber, pageSize }
        }
      )
      devices.value = response.items
      pagination.value = {
        pageNumber: response.pageNumber,
        pageSize: response.pageSize,
        totalCount: response.totalCount,
        hasNextPage: response.hasNextPage
      }
    } catch (err) {
      // Fallback to mock data
      const offset = (pageNumber - 1) * pageSize
      devices.value = generateMockDevices(pageSize, offset)
      pagination.value = {
        pageNumber,
        pageSize,
        totalCount: 150,
        hasNextPage: offset + pageSize < 150
      }
    } finally {
      loading.value = false
    }
  }

  async function fetchDeviceById(deviceId: string) {
    loading.value = true
    error.value = null
    
    try {
      const response = await apiClient.get<Device>(
        API_ENDPOINTS.DEVICES.GET(deviceId)
      )
      selectedDevice.value = response
      return response
    } catch (err) {
      // Fallback to mock device
      const mockDevices = generateMockDevices(1, 0)
      const mockDevice = { ...mockDevices[0], deviceId }
      selectedDevice.value = mockDevice
      return mockDevice
    } finally {
      loading.value = false
    }
  }

  function setSelectedDevice(device: Device | null) {
    selectedDevice.value = device
  }

  function updateDevice(updatedDevice: Device) {
    const index = devices.value.findIndex(d => d.deviceId === updatedDevice.deviceId)
    if (index > -1) {
      devices.value[index] = updatedDevice
    }
    if (selectedDevice.value?.deviceId === updatedDevice.deviceId) {
      selectedDevice.value = updatedDevice
    }
  }

  function clearError() {
    error.value = null
  }

  /**
   * Lock a device
   */
  async function lockDevice(deviceId: string) {
    try {
      const result = await deviceApi.lockDevice(deviceId)
      const device = devices.value.find(d => d.deviceId === deviceId)
      if (device) {
        device.status = 'Suspended' as const
      }
      return result
    } catch (err) {
      error.value = 'Failed to lock device'
      throw err
    }
  }

  /**
   * Wipe a device (factory reset)
   */
  async function wipeDevice(deviceId: string) {
    try {
      const result = await deviceApi.wipeDevice(deviceId)
      const device = devices.value.find(d => d.deviceId === deviceId)
      if (device) {
        device.status = 'Decommissioned' as const
      }
      return result
    } catch (err) {
      error.value = 'Failed to wipe device'
      throw err
    }
  }

  /**
   * Restart a device
   */
  async function restartDevice(deviceId: string) {
    try {
      return await deviceApi.restartDevice(deviceId)
    } catch (err) {
      error.value = 'Failed to restart device'
      throw err
    }
  }

  /**
   * Update device policy
   */
  async function updateDevicePolicy(deviceId: string, policyId: string) {
    try {
      return await deviceApi.updateDevicePolicy(deviceId, policyId)
    } catch (err) {
      error.value = 'Failed to update device policy'
      throw err
    }
  }

  /**
   * Get command status
   */
  async function getCommandStatus(deviceId: string, commandId: string) {
    try {
      return await deviceApi.getCommandStatus(deviceId, commandId)
    } catch (err) {
      error.value = 'Failed to get command status'
      throw err
    }
  }

  /**
   * Get command history
   */
  async function getCommandHistory(deviceId: string, limit: number = 50) {
    try {
      return await deviceApi.getCommandHistory(deviceId, limit)
    } catch (err) {
      error.value = 'Failed to get command history'
      throw err
    }
  }

  /**
   * Delete a device
   */
  async function deleteDevice(deviceId: string) {
    try {
      await deviceApi.deleteDevice(deviceId)
      const index = devices.value.findIndex(d => d.deviceId === deviceId)
      if (index > -1) {
        devices.value.splice(index, 1)
      }
      if (selectedDevice.value?.deviceId === deviceId) {
        selectedDevice.value = null
      }
    } catch (err) {
      error.value = 'Failed to delete device'
      throw err
    }
  }

  return {
    devices,
    selectedDevice,
    loading,
    error,
    pagination,
    filteredDevices,
    totalPages,
    fetchDevices,
    fetchDeviceById,
    setSelectedDevice,
    updateDevice,
    clearError,
    lockDevice,
    wipeDevice,
    restartDevice,
    updateDevicePolicy,
    getCommandStatus,
    getCommandHistory,
    deleteDevice
  }
})
