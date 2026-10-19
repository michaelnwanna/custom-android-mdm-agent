/**
 * Composable for device queries and filtering
 */

import { ref, computed, onMounted } from 'vue'
import * as deviceApi from '@/api/deviceApi'
import type { Device } from '@/types'

export interface UseDevicesOptions {
  pageSize?: number
  autoLoad?: boolean
}

export function useDevices(options: UseDevicesOptions = {}) {
  const {
    pageSize = 20,
    autoLoad = true
  } = options

  const devices = ref<Device[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const currentPage = ref(1)
  const totalCount = ref(0)
  const hasNextPage = ref(false)

  // Search and filter state
  const searchQuery = ref('')
  const statusFilter = ref<string | null>(null)
  const sortBy = ref<string>('lastSyncDate')
  const sortOrder = ref<'asc' | 'desc'>('desc')

  // Computed properties
  const totalPages = computed(() => Math.ceil(totalCount.value / pageSize))

  const displayedDevices = computed(() => {
    let filtered = [...devices.value]

    // Apply search filter
    if (searchQuery.value) {
      const query = searchQuery.value.toLowerCase()
      filtered = filtered.filter(d =>
        d.deviceName?.toLowerCase().includes(query) ||
        d.deviceId.toLowerCase().includes(query) ||
        d.model?.toLowerCase().includes(query)
      )
    }

    // Apply status filter
    if (statusFilter.value) {
      filtered = filtered.filter(d => d.status === statusFilter.value)
    }

    return filtered
  })

  /**
   * Fetch devices with current filters and pagination
   */
  async function fetchDevices(page: number = 1) {
    loading.value = true
    error.value = null
    currentPage.value = page

    try {
      const response = await deviceApi.fetchDevices({
        pageNumber: page,
        pageSize,
        status: statusFilter.value || undefined,
        search: searchQuery.value || undefined,
        sortBy: sortBy.value,
        sortOrder: sortOrder.value
      })

      devices.value = response.items || []
      totalCount.value = response.totalCount || 0
      hasNextPage.value = response.hasNextPage || false
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch devices'
      console.error('Error fetching devices:', err)
    } finally {
      loading.value = false
    }
  }

  /**
   * Fetch a specific device
   */
  async function fetchDevice(deviceId: string): Promise<Device | null> {
    loading.value = true
    error.value = null

    try {
      return await deviceApi.fetchDevice(deviceId)
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch device'
      return null
    } finally {
      loading.value = false
    }
  }

  /**
   * Set search query and reset to page 1
   */
  function setSearch(query: string) {
    searchQuery.value = query
    currentPage.value = 1
  }

  /**
   * Set status filter and reset to page 1
   */
  function setStatusFilter(status: string | null) {
    statusFilter.value = status
    currentPage.value = 1
  }

  /**
   * Set sort options
   */
  function setSort(field: string, order: 'asc' | 'desc' = 'desc') {
    sortBy.value = field
    sortOrder.value = order
  }

  /**
   * Go to next page
   */
  function nextPage() {
    if (hasNextPage.value) {
      fetchDevices(currentPage.value + 1)
    }
  }

  /**
   * Go to previous page
   */
  function previousPage() {
    if (currentPage.value > 1) {
      fetchDevices(currentPage.value - 1)
    }
  }

  /**
   * Go to specific page
   */
  function goToPage(page: number) {
    if (page > 0 && page <= totalPages.value) {
      fetchDevices(page)
    }
  }

  /**
   * Refresh current page
   */
  function refresh() {
    fetchDevices(currentPage.value)
  }

  /**
   * Clear all filters
   */
  function clearFilters() {
    searchQuery.value = ''
    statusFilter.value = null
    sortBy.value = 'lastSyncDate'
    sortOrder.value = 'desc'
    currentPage.value = 1
  }

  // Auto-load on mount if enabled
  onMounted(() => {
    if (autoLoad) {
      fetchDevices(1)
    }
  })

  return {
    // State
    devices: displayedDevices,
    loading,
    error,
    currentPage,
    totalCount,
    totalPages,
    hasNextPage,

    // Filters
    searchQuery,
    statusFilter,
    sortBy,
    sortOrder,

    // Methods
    fetchDevices,
    fetchDevice,
    setSearch,
    setStatusFilter,
    setSort,
    nextPage,
    previousPage,
    goToPage,
    refresh,
    clearFilters
  }
}
