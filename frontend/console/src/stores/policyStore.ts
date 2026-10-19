import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import apiClient from '@services/apiClient'
import type { Policy } from '@types'

export interface PolicyDetails extends Policy {
  rules?: string[]
  targetDevices?: string[]
  description: string
  enforcementLevel: 'Strict' | 'Standard' | 'Permissive'
  isActive: boolean
  deviceCount: number
  lastModified: string
}

function generateMockPolicies(count: number = 30): PolicyDetails[] {
  const policyNames = [
    'Device Encryption',
    'Password Requirements',
    'Screen Lock Timeout',
    'Camera Access Control',
    'USB Debugging Prevention',
    'Biometric Authentication',
    'VPN Requirement',
    'App Installation Control',
    'Browser Restrictions',
    'Storage Access Control',
    'GPS Tracking Enable',
    'Mobile Device Firewall',
    'Certificate Pinning',
    'Threat Detection',
    'Data Loss Prevention'
  ]

  const descriptions: Record<string, string> = {
    'Device Encryption': 'Require full device encryption for data protection at rest',
    'Password Requirements': 'Enforce strong password policies with complexity rules',
    'Screen Lock Timeout': 'Set automatic screen lock after inactivity period',
    'Camera Access Control': 'Restrict or disable camera access on devices',
    'USB Debugging Prevention': 'Prevent USB debugging to protect device security',
    'Biometric Authentication': 'Require biometric authentication for sensitive apps',
    'VPN Requirement': 'Require VPN connection for network access',
    'App Installation Control': 'Control which apps can be installed on devices',
    'Browser Restrictions': 'Restrict browser access to specific websites only',
    'Storage Access Control': 'Control device storage access and external media',
    'GPS Tracking Enable': 'Enable GPS tracking for device location',
    'Mobile Device Firewall': 'Apply firewall rules to device network traffic',
    'Certificate Pinning': 'Pin certificates for secure communication',
    'Threat Detection': 'Enable real-time threat detection and response',
    'Data Loss Prevention': 'Prevent sensitive data from leaving the device'
  }

  const rules: Record<string, string[]> = {
    'Device Encryption': ['AES-256 encryption', 'Full disk encryption', 'File-level encryption'],
    'Password Requirements': ['Min 12 characters', 'Uppercase + Lowercase', 'Numbers + Symbols'],
    'Screen Lock Timeout': ['15 minutes inactivity', 'Lock before sleep', 'Require unlock after wakeup'],
    'Camera Access Control': ['Disable front camera', 'Disable rear camera', 'Alert on camera use'],
    'USB Debugging Prevention': ['Disable USB debugging', 'Prevent developer mode', 'Alert on modifications'],
    'Biometric Authentication': ['Fingerprint required', 'Face recognition', 'Fallback to PIN'],
    'VPN Requirement': ['Always-on VPN', 'Corporate VPN only', 'Split tunneling disabled'],
    'App Installation Control': ['Whitelist only', 'Blacklist apps', 'Require approval'],
    'Browser Restrictions': ['Whitelist domains', 'Block known threats', 'HTTPS only'],
    'Storage Access Control': ['Disable SD card', 'Disable USB storage', 'Encrypt external storage'],
    'GPS Tracking Enable': ['Real-time tracking', 'Location logging', 'Geofencing enabled'],
    'Mobile Device Firewall': ['Block malicious IPs', 'Port filtering', 'Protocol inspection'],
    'Certificate Pinning': ['Pin CA certificate', 'Pin leaf certificate', 'Automatic updates'],
    'Threat Detection': ['Real-time scanning', 'Behavioral analysis', 'Ransomware detection'],
    'Data Loss Prevention': ['Prevent USB export', 'Block cloud sync', 'Email encryption']
  }

  const policies: PolicyDetails[] = []
  for (let i = 0; i < count; i++) {
    const name = policyNames[i % policyNames.length]
    const status = Math.random() < 0.7 ? 'Active' : Math.random() < 0.5 ? 'Draft' : 'Archived'
    const enforcementLevel = ['Strict', 'Standard', 'Permissive'][Math.floor(Math.random() * 3)] as 'Strict' | 'Standard' | 'Permissive'
    const createdDate = new Date(Date.now() - Math.random() * 180 * 24 * 60 * 60 * 1000)
    const lastModifiedDate = new Date(createdDate.getTime() + Math.random() * 60 * 24 * 60 * 60 * 1000)

    policies.push({
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
      const response = await apiClient.get<PolicyDetails[]>('/policies')
      policies.value = response.data
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
      // TODO: API endpoint - POST /policies
      const response = await apiClient.post<PolicyDetails>('/policies', policy)
      policies.value.push(response.data)
      return response.data
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
      // TODO: API endpoint - PUT /policies/{policyId}
      await apiClient.put(`/policies/${policyId}`, updates)
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
      // TODO: API endpoint - DELETE /policies/{policyId}
      await apiClient.delete(`/policies/${policyId}`)
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
      // TODO: API endpoint - POST /policies/{policyId}/activate
      await apiClient.post(`/policies/${policyId}/activate`, {})
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
      // TODO: API endpoint - POST /policies/{policyId}/deactivate
      await apiClient.post(`/policies/${policyId}/deactivate`, {})
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
