import { apiClient } from '@/api/client'
import type { Policy } from '@types'

export interface PolicyQueryParams {
  page?: number
  pageSize?: number
  type?: string
  status?: 'Active' | 'Archived' | 'Draft'
  search?: string
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
}

export interface CreatePolicyInput {
  name: string
  description: string
  type: string
  settings: Record<string, any>
}

export interface UpdatePolicyInput {
  name?: string
  description?: string
  settings?: Record<string, any>
  status?: string
}

export interface PolicyAssignment {
  deviceIds?: string[]
  groupIds?: string[]
}

export interface PolicyVersion {
  versionId: string
  version: number
  createdAt: string
  createdBy: string
  description: string
}

export interface PolicyCompliance {
  policyId: string
  totalDevices: number
  compliantDevices: number
  nonCompliantDevices: number
  unknownDevices: number
  compliancePercentage: number
  lastUpdated: string
}

export interface PaginatedPolicies {
  items: Policy[]
  totalCount: number
  pageNumber: number
  pageSize: number
  totalPages: number
}

/**
 * Fetch policies with pagination and filtering
 */
export async function fetchPolicies(options: PolicyQueryParams = {}): Promise<PaginatedPolicies> {
  try {
    const params = new URLSearchParams()
    if (options.page) params.append('page', options.page.toString())
    if (options.pageSize) params.append('pageSize', options.pageSize.toString())
    if (options.type) params.append('type', options.type)
    if (options.status) params.append('status', options.status)
    if (options.search) params.append('search', options.search)
    if (options.sortBy) params.append('sortBy', options.sortBy)
    if (options.sortOrder) params.append('sortOrder', options.sortOrder)

    const url = `/api/v1/policies?${params.toString()}`
    const response = await apiClient.get<PaginatedPolicies>(url)
    return response
  } catch (error) {
    console.error('Failed to fetch policies:', error)
    // Return mock data
    return generateMockPaginatedPolicies(options)
  }
}

/**
 * Get single policy by ID
 */
export async function fetchPolicyById(policyId: string): Promise<Policy | null> {
  try {
    const response = await apiClient.get<Policy>(`/api/v1/policies/${policyId}`)
    return response
  } catch (error) {
    console.error(`Failed to fetch policy ${policyId}:`, error)
    return null
  }
}

/**
 * Create new policy
 */
export async function createPolicy(policy: CreatePolicyInput): Promise<Policy> {
  try {
    const response = await apiClient.post<Policy>('/api/v1/policies', policy)
    return response
  } catch (error) {
    console.error('Failed to create policy:', error)
    throw error
  }
}

/**
 * Update policy
 */
export async function updatePolicy(
  policyId: string,
  updates: UpdatePolicyInput
): Promise<Policy> {
  try {
    const response = await apiClient.put<Policy>(`/api/v1/policies/${policyId}`, updates)
    return response
  } catch (error) {
    console.error(`Failed to update policy ${policyId}:`, error)
    throw error
  }
}

/**
 * Delete policy
 */
export async function deletePolicy(policyId: string): Promise<void> {
  try {
    await apiClient.delete(`/api/v1/policies/${policyId}`)
  } catch (error) {
    console.error(`Failed to delete policy ${policyId}:`, error)
    throw error
  }
}

/**
 * Assign policy to devices
 */
export async function assignPolicy(
  policyId: string,
  assignment: PolicyAssignment
): Promise<void> {
  try {
    await apiClient.post(`/api/v1/policies/${policyId}/assign`, assignment)
  } catch (error) {
    console.error(`Failed to assign policy ${policyId}:`, error)
    throw error
  }
}

/**
 * Revoke policy from devices
 */
export async function revokePolicy(policyId: string, deviceIds: string[]): Promise<void> {
  try {
    await apiClient.post(`/api/v1/policies/${policyId}/revoke`, { deviceIds })
  } catch (error) {
    console.error(`Failed to revoke policy ${policyId}:`, error)
    throw error
  }
}

/**
 * Get policy version history
 */
export async function getPolicyVersions(
  policyId: string,
  limit: number = 10
): Promise<PolicyVersion[]> {
  try {
    const response = await apiClient.get<PolicyVersion[]>(
      `/api/v1/policies/${policyId}/versions`,
      { params: { limit } }
    )
    return response
  } catch (error) {
    console.error(`Failed to fetch policy versions for ${policyId}:`, error)
    return []
  }
}

/**
 * Rollback policy to previous version
 */
export async function rollbackPolicy(policyId: string, versionId: string): Promise<Policy> {
  try {
    const response = await apiClient.post<Policy>(
      `/api/v1/policies/${policyId}/rollback`,
      { versionId }
    )
    return response
  } catch (error) {
    console.error(`Failed to rollback policy ${policyId}:`, error)
    throw error
  }
}

/**
 * Get policy compliance report
 */
export async function getPolicyCompliance(policyId: string): Promise<PolicyCompliance> {
  try {
    const response = await apiClient.get<PolicyCompliance>(
      `/api/v1/policies/${policyId}/compliance`
    )
    return response
  } catch (error) {
    console.error(`Failed to fetch compliance for policy ${policyId}:`, error)
    // Return mock compliance
    return {
      policyId,
      totalDevices: 0,
      compliantDevices: 0,
      nonCompliantDevices: 0,
      unknownDevices: 0,
      compliancePercentage: 0,
      lastUpdated: new Date().toISOString()
    }
  }
}

/**
 * Get policies for specific device
 */
export async function getDevicePolicies(deviceId: string): Promise<Policy[]> {
  try {
    const response = await apiClient.get<Policy[]>(`/api/v1/policies/device/${deviceId}`)
    return response
  } catch (error) {
    console.error(`Failed to fetch policies for device ${deviceId}:`, error)
    return []
  }
}

/**
 * Generate mock paginated policies for development/testing
 */
function generateMockPaginatedPolicies(options: PolicyQueryParams): PaginatedPolicies {
  const mockPolicies = generateMockPolicies(50)
  
  let filtered = mockPolicies
  
  // Apply filters
  if (options.type) {
    filtered = filtered.filter(p => p.type === options.type)
  }
  if (options.status) {
    filtered = filtered.filter(p => p.status === options.status)
  }
  if (options.search) {
    const search = options.search.toLowerCase()
    filtered = filtered.filter(p => 
      p.name.toLowerCase().includes(search) ||
      p.description.toLowerCase().includes(search)
    )
  }

  // Sort
  if (options.sortBy === 'name') {
    filtered.sort((a, b) => a.name.localeCompare(b.name))
  } else {
    filtered.sort((a, b) => new Date(b.lastUpdated).getTime() - new Date(a.lastUpdated).getTime())
  }

  if (options.sortOrder === 'asc') {
    filtered.reverse()
  }

  // Paginate
  const pageNumber = options.page || 1
  const pageSize = options.pageSize || 20
  const startIndex = (pageNumber - 1) * pageSize
  const endIndex = startIndex + pageSize
  const items = filtered.slice(startIndex, endIndex)
  const totalPages = Math.ceil(filtered.length / pageSize)

  return {
    items,
    totalCount: filtered.length,
    pageNumber,
    pageSize,
    totalPages
  }
}

/**
 * Generate mock policies for development
 */
function generateMockPolicies(count: number): Policy[] {
  const policyTypes = ['Security', 'Compliance', 'Performance', 'Custom']
  const policyStatuses = ['Active', 'Archived', 'Draft']
  const policyNames = [
    'Enterprise Security Policy',
    'GDPR Compliance Policy',
    'Mobile Device Protection',
    'Password Management Policy',
    'Data Encryption Policy',
    'App Restriction Policy',
    'Network Security Policy',
    'Update Management Policy',
    'Backup & Recovery Policy',
    'Device Hardening Policy'
  ]

  const policies: Policy[] = []
  for (let i = 0; i < count; i++) {
    const policyType = policyTypes[Math.floor(Math.random() * policyTypes.length)]
    const status = policyStatuses[Math.floor(Math.random() * policyStatuses.length)]
    const name = policyNames[Math.floor(Math.random() * policyNames.length)]
    const deviceCount = Math.floor(Math.random() * 200) + 10
    const compliantCount = Math.floor(deviceCount * (0.5 + Math.random() * 0.5))

    const createdAt = new Date(Date.now() - Math.random() * 90 * 24 * 60 * 60 * 1000)
    const lastUpdated = new Date(createdAt.getTime() + Math.random() * 30 * 24 * 60 * 60 * 1000)

    policies.push({
      policyId: `policy-${String(i + 1).padStart(6, '0')}`,
      name: `${name} #${i + 1}`,
      description: `${policyType} policy for managing device configuration and compliance`,
      type: policyType,
      status,
      version: Math.floor(Math.random() * 5) + 1,
      deviceCount,
      compliantDeviceCount: compliantCount,
      compliancePercentage: Math.round((compliantCount / deviceCount) * 100),
      createdAt: createdAt.toISOString(),
      lastUpdated: lastUpdated.toISOString(),
      createdBy: 'Admin',
      settings: {
        passwordLength: 8,
        encryptionEnabled: true,
        biometricRequired: false,
        cameraDisabled: false
      }
    } as Policy)
  }

  return policies
}
