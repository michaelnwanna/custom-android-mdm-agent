import { apiClient } from '@/api/client'
import { API_ENDPOINTS } from '@/api/endpoints'
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
  enrollmentIds: string[]
}

export interface PolicyAssignmentResult {
  enrollmentId: string
  assignmentId?: string
  assignedVersion?: number
  assignedAt?: string
  error?: string
}

export interface AssignmentStatus {
  enrollmentId: string
  status: 'queued' | 'failed'
  message: string
}

export interface PolicyManagementDevice {
  deviceId: string
  enrollmentId: string
  deviceName: string
  deviceState: string
  complianceStatus: 'compliant' | 'non-compliant' | 'pending' | 'unassigned'
  compliancePercentage: number
  lastChecked: string
  policyId?: string
  policyName?: string
  assignedVersion?: number
  acknowledgedVersion?: number
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
  const params = new URLSearchParams()
  if (options.page) params.append('page', options.page.toString())
  if (options.pageSize) params.append('pageSize', options.pageSize.toString())
  if (options.status) params.append('status', options.status)
  if (options.type) params.append('type', options.type)
  if (options.search) params.append('search', options.search)
  if (options.sortBy) params.append('sortBy', options.sortBy)
  if (options.sortOrder) params.append('sortOrder', options.sortOrder)

  const query = params.toString()
  return apiClient.get<PaginatedPolicies>(
    `${API_ENDPOINTS.POLICIES.LIST}${query ? `?${query}` : ''}`
  )
}

/**
 * Get single policy by ID
 */
export async function fetchPolicyById(policyId: string): Promise<Policy> {
  return apiClient.get<Policy>(API_ENDPOINTS.POLICIES.GET(policyId))
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
): Promise<PolicyAssignmentResult[]> {
  return Promise.all(assignment.enrollmentIds.map(async enrollmentId => {
    try {
      return await assignPolicyToDevice(policyId, enrollmentId)
    } catch (error) {
      return {
        enrollmentId,
        error: error instanceof Error ? error.message : 'Assignment failed'
      }
    }
  }))
}

export async function assignPolicyToDevice(
  policyId: string,
  enrollmentId: string
): Promise<PolicyAssignmentResult> {
  const response = await apiClient.post<{
    assignmentId: string
    assignedVersion: number
    assignedAt: string
  }>(API_ENDPOINTS.POLICIES.ASSIGN, { enrollmentId, policyId })
  return { enrollmentId, ...response }
}

export async function fetchAssignableDevices(): Promise<PolicyManagementDevice[]> {
  return apiClient.get<PolicyManagementDevice[]>(API_ENDPOINTS.POLICIES.ASSIGNABLE_DEVICES)
}

export async function fetchComplianceReport(): Promise<PolicyManagementDevice[]> {
  return apiClient.get<PolicyManagementDevice[]>(API_ENDPOINTS.POLICIES.COMPLIANCE_REPORT)
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
  return apiClient.get<PolicyCompliance>(`/api/v1/policies/${policyId}/compliance`)
}

/**
 * Get policies for specific device
 */
export async function getDevicePolicies(deviceId: string): Promise<Policy[]> {
  return apiClient.get<Policy[]>(`/api/v1/policies/device/${deviceId}`)
}
