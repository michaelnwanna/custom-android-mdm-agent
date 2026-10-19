import { ref, computed, onMounted } from 'vue'
import * as policyApi from '@/api/policyApi'
import type { Policy } from '@types'
import type { PolicyQueryParams } from '@/api/policyApi'

interface UsePoliciesOptions {
  pageSize?: number
  autoLoad?: boolean
}

/**
 * Composable for policy queries with pagination and filtering
 */
export function usePolicies(options: UsePoliciesOptions = {}) {
  const pageSize = options.pageSize || 20
  const autoLoad = options.autoLoad !== false

  // State
  const policies = ref<Policy[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const totalCount = ref(0)
  const pageNumber = ref(1)
  const totalPages = ref(1)

  // Filters
  const search = ref('')
  const type = ref('')
  const status = ref('')
  const sortBy = ref('date') // 'date' or 'name'
  const sortOrder = ref<'asc' | 'desc'>('desc')

  // Computed
  const queryParams = computed<PolicyQueryParams>(() => ({
    page: pageNumber.value,
    pageSize,
    ...(type.value && { type: type.value }),
    ...(status.value && { status: status.value as any }),
    ...(search.value && { search: search.value }),
    sortBy: sortBy.value,
    sortOrder: sortOrder.value
  }))

  const hasFilters = computed(() => 
    search.value || type.value || status.value
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
    if (totalCount.value === 0) return 'No policies'
    const start = (pageNumber.value - 1) * pageSize + 1
    const end = Math.min(pageNumber.value * pageSize, totalCount.value)
    return `${start}-${end} of ${totalCount.value}`
  })

  const totalDevicesDeployed = computed(() => 
    policies.value.reduce((sum, p) => sum + (p.deviceCount || 0), 0)
  )

  const averageCompliance = computed(() => {
    if (policies.value.length === 0) return 0
    const total = policies.value.reduce((sum, p) => sum + (p.compliancePercentage || 0), 0)
    return Math.round(total / policies.value.length)
  })

  // Methods
  const fetchPolicies = async () => {
    loading.value = true
    error.value = null
    try {
      const result = await policyApi.fetchPolicies(queryParams.value)
      policies.value = result.items
      totalCount.value = result.totalCount
      totalPages.value = result.totalPages
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch policies'
      console.error('Policy fetch error:', err)
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

  const updateType = (value: string) => {
    type.value = value
    pageNumber.value = 1
  }

  const updateStatus = (value: string) => {
    status.value = value
    pageNumber.value = 1
  }

  const clearFilters = () => {
    search.value = ''
    type.value = ''
    status.value = ''
    pageNumber.value = 1
  }

  const setSortBy = (field: string, order: 'asc' | 'desc' = 'desc') => {
    sortBy.value = field
    sortOrder.value = order
    pageNumber.value = 1
  }

  const deletePolicy = async (policyId: string) => {
    try {
      await policyApi.deletePolicy(policyId)
      policies.value = policies.value.filter(p => p.policyId !== policyId)
      totalCount.value--
      return true
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to delete policy'
      return false
    }
  }

  const assignPolicy = async (policyId: string, enrollmentIds: string[]) => {
    try {
      const results = await policyApi.assignPolicy(policyId, { enrollmentIds })
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.deviceCount = (policy.deviceCount || 0) +
          results.filter(result => !result.error).length
      }
      return results
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to assign policy'
      return []
    }
  }

  const revokePolicy = async (policyId: string, deviceIds: string[]) => {
    try {
      await policyApi.revokePolicy(policyId, deviceIds)
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.deviceCount = Math.max(0, (policy.deviceCount || 0) - deviceIds.length)
      }
      return true
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to revoke policy'
      return false
    }
  }

  const rollbackPolicy = async (policyId: string, versionId: string) => {
    try {
      const policy = await policyApi.rollbackPolicy(policyId, versionId)
      const index = policies.value.findIndex(p => p.policyId === policyId)
      if (index > -1) {
        policies.value[index] = policy
      }
      return true
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to rollback policy'
      return false
    }
  }

  const updatePolicy = (policyId: string, updates: Partial<Policy>) => {
    const policy = policies.value.find(p => p.policyId === policyId)
    if (policy) {
      Object.assign(policy, updates)
    }
  }

  const addPolicy = (policy: Policy) => {
    const exists = policies.value.find(p => p.policyId === policy.policyId)
    if (!exists) {
      policies.value.unshift(policy)
      totalCount.value++
    }
  }

  // Auto-load on mount
  if (autoLoad) {
    onMounted(() => {
      fetchPolicies()
    })
  }

  return {
    // State
    policies,
    loading,
    error,
    totalCount,
    pageNumber,
    totalPages,
    
    // Filters
    search,
    type,
    status,
    sortBy,
    sortOrder,
    
    // Computed
    queryParams,
    hasFilters,
    pageRange,
    displayText,
    totalDevicesDeployed,
    averageCompliance,
    
    // Methods
    fetchPolicies,
    goToPage,
    nextPage,
    prevPage,
    updateSearch,
    updateType,
    updateStatus,
    clearFilters,
    setSortBy,
    deletePolicy,
    assignPolicy,
    revokePolicy,
    rollbackPolicy,
    updatePolicy,
    addPolicy
  }
}
