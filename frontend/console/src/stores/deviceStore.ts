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
    totalCount: 0,
    hasNextPage: false
  })

  const filteredDevices = computed(() => devices.value)
  
  const totalPages = computed(() => 
    Math.ceil(pagination.value.totalCount / pagination.value.pageSize)
  )

  async function fetchDevices(pageNumber = 1, pageSize = 20) {
    loading.value = true
    error.value = null
    
    try {
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
      error.value = err instanceof Error ? err.message : 'Failed to fetch devices'
    } finally {
      loading.value = false
    }
  }

  async function fetchDeviceById(deviceId: string) {
    loading.value = true
    error.value = null
    
    try {
      const response = await apiClient.get<Device>(`/devices/${deviceId}`)
      selectedDevice.value = response.data
      return response.data
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch device'
    } finally {
      loading.value = false
    }
  }

  function setSelectedDevice(device: Device | null) {
    selectedDevice.value = device
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
    clearError
  }
})
