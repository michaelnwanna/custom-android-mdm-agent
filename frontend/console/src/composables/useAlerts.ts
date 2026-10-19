import { ref, computed, onMounted } from 'vue'
import * as alertApi from '@/api/alertApi'
import type { HealthAlert, AlertQueryParams } from '@types'

interface UseAlertsOptions {
  pageSize?: number
  autoLoad?: boolean
}

/**
 * Composable for alert queries with pagination and filtering
 */
export function useAlerts(options: UseAlertsOptions = {}) {
  const pageSize = options.pageSize || 20
  const autoLoad = options.autoLoad !== false

  // State
  const alerts = ref<HealthAlert[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const totalCount = ref(0)
  const pageNumber = ref(1)
  const totalPages = ref(1)

  // Filters
  const search = ref('')
  const severity = ref<'Critical' | 'High' | 'Medium' | 'Low' | ''>('')
  const status = ref<'Open' | 'Resolved' | 'Acknowledged' | ''>('')
  const deviceId = ref('')
  const sortBy = ref('date') // 'date' or 'severity'
  const sortOrder = ref<'asc' | 'desc'>('desc')

  // Computed
  const queryParams = computed<AlertQueryParams>(() => ({
    page: pageNumber.value,
    pageSize,
    ...(severity.value && { severity: severity.value as any }),
    ...(status.value && { status: status.value as any }),
    ...(search.value && { search: search.value }),
    ...(deviceId.value && { deviceId: deviceId.value }),
    sortBy: sortBy.value,
    sortOrder: sortOrder.value
  }))

  const hasFilters = computed(() => 
    search.value || severity.value || status.value || deviceId.value
  )

  const pageRange = computed(() => {
    const delta = 2
    const range = []
    const rangeStart = Math.max(1, pageNumber.value - delta)
    const rangeEnd = Math.min(totalPages.value, pageNumber.value + delta)

    for (let i = rangeStart; i <= rangeEnd; i++) {
      range.push(i)
    }
    return range
  })

  const displayText = computed(() => {
    if (totalCount.value === 0) return 'No alerts'
    const start = (pageNumber.value - 1) * pageSize + 1
    const end = Math.min(pageNumber.value * pageSize, totalCount.value)
    return `${start}-${end} of ${totalCount.value}`
  })

  // Methods
  const fetchAlerts = async () => {
    loading.value = true
    error.value = null
    try {
      const result = await alertApi.fetchAlerts(queryParams.value)
      alerts.value = result.items
      totalCount.value = result.totalCount
      totalPages.value = result.totalPages
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch alerts'
      console.error('Alert fetch error:', err)
    } finally {
      loading.value = false
    }
  }

  const goToPage = (page: number) => {
    if (page >= 1 && page <= totalPages.value) {
      pageNumber.value = page
    }
  }

  const nextPage = () => {
    if (pageNumber.value < totalPages.value) {
      pageNumber.value++
    }
  }

  const prevPage = () => {
    if (pageNumber.value > 1) {
      pageNumber.value--
    }
  }

  const updateSearch = (value: string) => {
    search.value = value
    pageNumber.value = 1 // Reset to first page
  }

  const updateSeverity = (value: string) => {
    severity.value = value as 'Critical' | 'High' | 'Medium' | 'Low' | ''
    pageNumber.value = 1
  }

  const updateStatus = (value: string) => {
    status.value = value as 'Open' | 'Resolved' | 'Acknowledged' | ''
    pageNumber.value = 1
  }

  const updateDeviceId = (value: string) => {
    deviceId.value = value
    pageNumber.value = 1
  }

  const clearFilters = () => {
    search.value = ''
    severity.value = ''
    status.value = ''
    deviceId.value = ''
    pageNumber.value = 1
  }

  const setSortBy = (field: string, order: 'asc' | 'desc' = 'desc') => {
    sortBy.value = field
    sortOrder.value = order
    pageNumber.value = 1
  }

  const resolveAlert = async (alertId: string, resolution: string = 'Resolved') => {
    try {
      await alertApi.resolveAlert(alertId, { resolution })
      const alert = alerts.value.find(a => a.alertId === alertId)
      if (alert) {
        alert.resolved = true
        alert.resolvedAt = new Date().toISOString()
      }
      return true
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to resolve alert'
      return false
    }
  }

  const reopenAlert = async (alertId: string) => {
    try {
      await alertApi.reopenAlert(alertId)
      const alert = alerts.value.find(a => a.alertId === alertId)
      if (alert) {
        alert.resolved = false
        alert.resolvedAt = null
      }
      return true
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to reopen alert'
      return false
    }
  }

  const deleteAlert = async (alertId: string) => {
    try {
      await alertApi.deleteAlert(alertId)
      alerts.value = alerts.value.filter(a => a.alertId !== alertId)
      totalCount.value--
      return true
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to delete alert'
      return false
    }
  }

  const bulkResolveAlerts = async (alertIds: string[]) => {
    try {
      await alertApi.bulkResolveAlerts(alertIds)
      alertIds.forEach(id => {
        const alert = alerts.value.find(a => a.alertId === id)
        if (alert) {
          alert.resolved = true
          alert.resolvedAt = new Date().toISOString()
        }
      })
      return true
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to resolve alerts'
      return false
    }
  }

  const addAlert = (alert: HealthAlert) => {
    const exists = alerts.value.find(a => a.alertId === alert.alertId)
    if (!exists) {
      alerts.value.unshift(alert)
      totalCount.value++
    }
  }

  const updateAlert = (alertId: string, updates: Partial<HealthAlert>) => {
    const alert = alerts.value.find(a => a.alertId === alertId)
    if (alert) {
      Object.assign(alert, updates)
    }
  }

  // Auto-load on mount
  if (autoLoad) {
    onMounted(() => {
      fetchAlerts()
    })
  }

  return {
    // State
    alerts,
    loading,
    error,
    totalCount,
    pageNumber,
    totalPages,
    
    // Filters
    search,
    severity,
    status,
    deviceId,
    sortBy,
    sortOrder,
    
    // Computed
    queryParams,
    hasFilters,
    pageRange,
    displayText,
    
    // Methods
    fetchAlerts,
    goToPage,
    nextPage,
    prevPage,
    updateSearch,
    updateSeverity,
    updateStatus,
    updateDeviceId,
    clearFilters,
    setSortBy,
    resolveAlert,
    reopenAlert,
    deleteAlert,
    bulkResolveAlerts,
    addAlert,
    updateAlert
  }
}
