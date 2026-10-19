import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as policyApi from '@/api/policyApi'
import type { Policy } from '@types'

export const usePolicyStore = defineStore('policy', () => {
  const policies = ref<Policy[]>([])
  const selectedPolicy = ref<Policy | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const activePolicies = computed(() =>
    policies.value.filter(p => p.status === 'Active')
  )

  const policiesByType = computed(() => {
    const grouped: Record<string, Policy[]> = {}
    policies.value.forEach(policy => {
      const key = policy.type || 'Unknown'
      if (!grouped[key]) {
        grouped[key] = []
      }
      grouped[key].push(policy)
    })
    return grouped
  })

  const policiesByStatus = computed(() => {
    const grouped: Record<string, Policy[]> = {
      'Active': [],
      'Draft': [],
      'Archived': []
    }
    policies.value.forEach(policy => {
      grouped[policy.status].push(policy)
    })
    return grouped
  })

  const totalDevicesDeployed = computed(() =>
    policies.value.reduce((sum, p) => sum + (p.deviceCount || 0), 0)
  )

  const averageCompliance = computed(() => {
    if (policies.value.length === 0) return 0
    const total = policies.value.reduce((sum, p) => sum + (p.compliancePercentage || 0), 0)
    return Math.round(total / policies.value.length)
  })

  async function fetchPolicies() {
    loading.value = true
    error.value = null
    
    try {
      const result = await policyApi.fetchPolicies({ page: 1, pageSize: 100 })
      policies.value = result.items
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to fetch policies'
      console.error('Fetch policies error:', err)
    } finally {
      loading.value = false
    }
  }

  async function fetchPolicyById(policyId: string) {
    try {
      const policy = await policyApi.fetchPolicyById(policyId)
      if (policy) {
        selectedPolicy.value = policy
        return policy
      }
      return null
    } catch (err) {
      console.error(`Fetch policy error for ${policyId}:`, err)
      return null
    }
  }

  async function createPolicy(policy: any) {
    try {
      const newPolicy = await policyApi.createPolicy(policy)
      policies.value.unshift(newPolicy)
      return newPolicy
    } catch (err) {
      console.error('Create policy error:', err)
      throw err
    }
  }

  async function updatePolicy(policyId: string, updates: any) {
    try {
      const policy = await policyApi.updatePolicy(policyId, updates)
      const index = policies.value.findIndex(p => p.policyId === policyId)
      if (index > -1) {
        policies.value[index] = policy
      }
      if (selectedPolicy.value?.policyId === policyId) {
        selectedPolicy.value = policy
      }
      return policy
    } catch (err) {
      console.error('Update policy error:', err)
      throw err
    }
  }

  async function deletePolicy(policyId: string) {
    try {
      await policyApi.deletePolicy(policyId)
      policies.value = policies.value.filter(p => p.policyId !== policyId)
      if (selectedPolicy.value?.policyId === policyId) {
        selectedPolicy.value = null
      }
    } catch (err) {
      console.error('Delete policy error:', err)
      throw err
    }
  }

  async function assignPolicy(policyId: string, deviceIds: string[]) {
    try {
      await policyApi.assignPolicy(policyId, { deviceIds })
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.deviceCount = (policy.deviceCount || 0) + deviceIds.length
      }
    } catch (err) {
      console.error('Assign policy error:', err)
      throw err
    }
  }

  async function revokePolicy(policyId: string, deviceIds: string[]) {
    try {
      await policyApi.revokePolicy(policyId, deviceIds)
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.deviceCount = Math.max(0, (policy.deviceCount || 0) - deviceIds.length)
      }
    } catch (err) {
      console.error('Revoke policy error:', err)
      throw err
    }
  }

  async function rollbackPolicy(policyId: string, versionId: string) {
    try {
      const policy = await policyApi.rollbackPolicy(policyId, versionId)
      const index = policies.value.findIndex(p => p.policyId === policyId)
      if (index > -1) {
        policies.value[index] = policy
      }
      if (selectedPolicy.value?.policyId === policyId) {
        selectedPolicy.value = policy
      }
      return policy
    } catch (err) {
      console.error('Rollback policy error:', err)
      throw err
    }
  }

  async function getPolicyCompliance(policyId: string) {
    try {
      return await policyApi.getPolicyCompliance(policyId)
    } catch (err) {
      console.error('Get compliance error:', err)
      return null
    }
  }

  async function getPolicyVersions(policyId: string) {
    try {
      return await policyApi.getPolicyVersions(policyId)
    } catch (err) {
      console.error('Get versions error:', err)
      return []
    }
  }

  function clearError() {
    error.value = null
  }

  function clearSelectedPolicy() {
    selectedPolicy.value = null
  }

  return {
    // State
    policies,
    selectedPolicy,
    loading,
    error,
    
    // Computed
    activePolicies,
    policiesByType,
    policiesByStatus,
    totalDevicesDeployed,
    averageCompliance,
    
    // Methods
    fetchPolicies,
    fetchPolicyById,
    createPolicy,
    updatePolicy,
    deletePolicy,
    assignPolicy,
    revokePolicy,
    rollbackPolicy,
    getPolicyCompliance,
    getPolicyVersions,
    clearError,
    clearSelectedPolicy
  }
})
      policyId: `policy-${String(i + 1).padStart(6, '0')}`,
      name: `${name}${i > 14 ? ` (${i})` : ''}`,
      description: descriptions[name] || `Policy configuration for ${name}`,
      status,
      createdAt: createdDate.toISOString(),
      updatedAt: lastModifiedDate.toISOString(),
      enforcementLevel,
      isActive: status === 'Active',
      deviceCount: Math.floor(Math.random() * 500) + 10,
      rules: rules[name] || [],
      targetDevices: Array.from({ length: Math.floor(Math.random() * 50) + 5 }, (_, i) => `device-${String(i + 1).padStart(6, '0')}`)
    })
  }

  return policies
}

export const usePolicyStore = defineStore('policy', () => {
  const policies = ref<PolicyDetails[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  const activePolicies = computed(() =>
    policies.value.filter(p => p.status === 'Active')
  )

  const draftPolicies = computed(() =>
    policies.value.filter(p => p.status === 'Draft')
  )

  const archivedPolicies = computed(() =>
    policies.value.filter(p => p.status === 'Archived')
  )

  const policiesByEnforcement = computed(() => {
    const grouped: Record<string, PolicyDetails[]> = {
      'Strict': [],
      'Standard': [],
      'Permissive': []
    }
    policies.value.forEach(policy => {
      grouped[policy.enforcementLevel].push(policy)
    })
    return grouped
  })

  async function fetchPolicies() {
    loading.value = true
    error.value = null
    
    try {
      const response = await apiClient.get<PolicyDetails[]>(API_ENDPOINTS.POLICIES.LIST)
      policies.value = response
    } catch (err) {
      // Fallback to mock data
      policies.value = generateMockPolicies(30)
      error.value = null // Don't show error if we have mock data
    } finally {
      loading.value = false
    }
  }

  async function createPolicy(policy: Omit<PolicyDetails, 'policyId' | 'createdAt' | 'updatedAt'>) {
    try {
      const response = await apiClient.post<PolicyDetails>(API_ENDPOINTS.POLICIES.CREATE, policy)
      policies.value.push(response)
      return response
    } catch (err) {
      // Fallback: create locally
      const newPolicy: PolicyDetails = {
        ...policy,
        policyId: `policy-${Date.now()}`,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      }
      policies.value.push(newPolicy)
      return newPolicy
    }
  }

  async function updatePolicy(policyId: string, updates: Partial<PolicyDetails>) {
    try {
      await apiClient.put(API_ENDPOINTS.POLICIES.UPDATE(policyId), updates)
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        Object.assign(policy, updates)
        policy.updatedAt = new Date().toISOString()
      }
    } catch (err) {
      // Fallback: update locally
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        Object.assign(policy, updates)
        policy.updatedAt = new Date().toISOString()
      }
    }
  }

  async function deletePolicy(policyId: string) {
    try {
      await apiClient.delete(API_ENDPOINTS.POLICIES.DELETE(policyId))
      const index = policies.value.findIndex(p => p.policyId === policyId)
      if (index > -1) {
        policies.value.splice(index, 1)
      }
    } catch (err) {
      // Fallback: delete locally
      const index = policies.value.findIndex(p => p.policyId === policyId)
      if (index > -1) {
        policies.value.splice(index, 1)
      }
    }
  }

  async function activatePolicy(policyId: string) {
    try {
      await apiClient.post(API_ENDPOINTS.POLICIES.ACTIVATE(policyId), {})
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.status = 'Active'
        policy.isActive = true
        policy.updatedAt = new Date().toISOString()
      }
    } catch (err) {
      // Fallback: update locally
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.status = 'Active'
        policy.isActive = true
        policy.updatedAt = new Date().toISOString()
      }
    }
  }

  async function deactivatePolicy(policyId: string) {
    try {
      await apiClient.post(API_ENDPOINTS.POLICIES.DEACTIVATE(policyId), {})
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.status = 'Draft'
        policy.isActive = false
        policy.updatedAt = new Date().toISOString()
      }
    } catch (err) {
      // Fallback: update locally
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.status = 'Draft'
        policy.isActive = false
        policy.updatedAt = new Date().toISOString()
      }
    }
  }

  function getPolicyById(policyId: string): PolicyDetails | undefined {
    return policies.value.find(p => p.policyId === policyId)
  }

  function clearError() {
    error.value = null
  }

  return {
    policies,
    loading,
    error,
    activePolicies,
    draftPolicies,
    archivedPolicies,
    policiesByEnforcement,
    fetchPolicies,
    createPolicy,
    updatePolicy,
    deletePolicy,
    activatePolicy,
    deactivatePolicy,
    getPolicyById,
    clearError
  }
})
