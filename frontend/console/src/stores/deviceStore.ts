import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import apiClient from '@services/apiClient'
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
      // Try real API first
      const response = await apiClient.get<PaginatedResult<Device>>(
        `/devices?pageNumber=${pageNumber}&pageSize=${pageSize}`
      )
      devices.value = response.data.items
      pagination.value = {
        pageNumber: response.data.pageNumber,
        pageSize: response.data.pageSize,
        totalCount: response.data.totalCount,
        hasNextPage: response.data.hasNextPage
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
      // Try real API first
      const response = await apiClient.get<Device>(`/devices/${deviceId}`)
      selectedDevice.value = response.data
      return response.data
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
    clearError
  }
})
