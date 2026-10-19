import { apiClient } from '@/api/client'
import type { HealthAlert } from '@types'

export interface AlertQueryParams {
  page?: number
  pageSize?: number
  severity?: 'Critical' | 'High' | 'Medium' | 'Low'
  status?: 'Open' | 'Resolved' | 'Acknowledged'
  search?: string
  deviceId?: string
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
}

export interface CreateAlertInput {
  deviceId: string
  severity: string
  type: string
  message: string
}

export interface UpdateAlertInput {
  severity?: string
  message?: string
  acknowledged?: boolean
}

export interface AlertResolution {
  resolution: string
  timestamp?: string
}

export interface PaginatedAlerts {
  items: HealthAlert[]
  totalCount: number
  pageNumber: number
  pageSize: number
  totalPages: number
}

export interface AlertHistory {
  alertId: string
  changes: Array<{
    field: string
    oldValue: string
    newValue: string
    timestamp: string
  }>
}

/**
 * Fetch alerts with pagination and filtering
 */
export async function fetchAlerts(options: AlertQueryParams = {}): Promise<PaginatedAlerts> {
  try {
    const params = new URLSearchParams()
    if (options.page) params.append('page', options.page.toString())
    if (options.pageSize) params.append('pageSize', options.pageSize.toString())
    if (options.severity) params.append('severity', options.severity)
    if (options.status) params.append('status', options.status)
    if (options.search) params.append('search', options.search)
    if (options.deviceId) params.append('deviceId', options.deviceId)
    if (options.sortBy) params.append('sortBy', options.sortBy)
    if (options.sortOrder) params.append('sortOrder', options.sortOrder)

    const url = `/api/v1/alerts?${params.toString()}`
    const response = await apiClient.get<PaginatedAlerts>(url)
    return response
  } catch (error) {
    console.error('Failed to fetch alerts:', error)
    // Return mock data
    return generateMockPaginatedAlerts(options)
  }
}

/**
 * Get single alert by ID
 */
export async function fetchAlertById(alertId: string): Promise<HealthAlert | null> {
  try {
    const response = await apiClient.get<HealthAlert>(`/api/v1/alerts/${alertId}`)
    return response
  } catch (error) {
    console.error(`Failed to fetch alert ${alertId}:`, error)
    return null
  }
}

/**
 * Create new alert
 */
export async function createAlert(alert: CreateAlertInput): Promise<HealthAlert> {
  try {
    const response = await apiClient.post<HealthAlert>('/api/v1/alerts', alert)
    return response
  } catch (error) {
    console.error('Failed to create alert:', error)
    throw error
  }
}

/**
 * Update alert
 */
export async function updateAlert(
  alertId: string,
  updates: UpdateAlertInput
): Promise<HealthAlert> {
  try {
    const response = await apiClient.put<HealthAlert>(`/api/v1/alerts/${alertId}`, updates)
    return response
  } catch (error) {
    console.error(`Failed to update alert ${alertId}:`, error)
    throw error
  }
}

/**
 * Delete alert
 */
export async function deleteAlert(alertId: string): Promise<void> {
  try {
    await apiClient.delete(`/api/v1/alerts/${alertId}`)
  } catch (error) {
    console.error(`Failed to delete alert ${alertId}:`, error)
    throw error
  }
}

/**
 * Resolve single alert
 */
export async function resolveAlert(
  alertId: string,
  resolution: AlertResolution = { resolution: 'Resolved' }
): Promise<HealthAlert> {
  try {
    const response = await apiClient.post<HealthAlert>(
      `/api/v1/alerts/${alertId}/resolve`,
      resolution
    )
    return response
  } catch (error) {
    console.error(`Failed to resolve alert ${alertId}:`, error)
    throw error
  }
}

/**
 * Reopen resolved alert
 */
export async function reopenAlert(alertId: string): Promise<HealthAlert> {
  try {
    const response = await apiClient.post<HealthAlert>(`/api/v1/alerts/${alertId}/reopen`, {})
    return response
  } catch (error) {
    console.error(`Failed to reopen alert ${alertId}:`, error)
    throw error
  }
}

/**
 * Bulk resolve multiple alerts
 */
export async function bulkResolveAlerts(alertIds: string[]): Promise<void> {
  try {
    await apiClient.post('/api/v1/alerts/batch/resolve', { alertIds })
  } catch (error) {
    console.error('Failed to bulk resolve alerts:', error)
    throw error
  }
}

/**
 * Get alert history/timeline
 */
export async function getAlertHistory(alertId: string, limit: number = 10): Promise<AlertHistory> {
  try {
    const response = await apiClient.get<AlertHistory>(`/api/v1/alerts/${alertId}/history`, {
      params: { limit }
    })
    return response
  } catch (error) {
    console.error(`Failed to fetch alert history for ${alertId}:`, error)
    // Return empty history
    return {
      alertId,
      changes: []
    }
  }
}

/**
 * Get alerts for specific device
 */
export async function getDeviceAlerts(deviceId: string, limit: number = 50): Promise<HealthAlert[]> {
  try {
    const response = await apiClient.get<HealthAlert[]>(`/api/v1/alerts/device/${deviceId}`, {
      params: { limit }
    })
    return response
  } catch (error) {
    console.error(`Failed to fetch alerts for device ${deviceId}:`, error)
    return []
  }
}

/**
 * Generate mock paginated alerts for development/testing
 */
function generateMockPaginatedAlerts(options: AlertQueryParams): PaginatedAlerts {
  const mockAlerts = generateMockAlerts(100)
  
  let filtered = mockAlerts
  
  // Apply filters
  if (options.severity) {
    filtered = filtered.filter(a => a.severity === options.severity)
  }
  if (options.status) {
    const isOpen = options.status === 'Open'
    filtered = filtered.filter(a => !a.resolvedAt === isOpen)
  }
  if (options.search) {
    const search = options.search.toLowerCase()
    filtered = filtered.filter(a => 
      a.message.toLowerCase().includes(search) ||
      a.deviceName.toLowerCase().includes(search)
    )
  }
  if (options.deviceId) {
    filtered = filtered.filter(a => a.deviceId === options.deviceId)
  }

  // Sort
  if (options.sortBy === 'severity') {
    const severityOrder = { Critical: 0, High: 1, Medium: 2, Low: 3 }
    filtered.sort((a, b) => severityOrder[a.severity as keyof typeof severityOrder] - severityOrder[b.severity as keyof typeof severityOrder])
  } else {
    filtered.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime())
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
 * Generate mock alerts for development
 */
function generateMockAlerts(count: number): HealthAlert[] {
  const alertTypes = [
    'Low Battery',
    'Storage Critical',
    'Memory Warning',
    'Connectivity Lost',
    'Security Alert',
    'Policy Violation',
    'OS Update Available',
    'App Malware Detected',
    'Certificate Expired',
    'Configuration Drift'
  ]

  const messages: Record<string, string[]> = {
    'Low Battery': ['Battery below 15%', 'Battery critical - 5% remaining'],
    'Storage Critical': ['Storage 95% full', 'No space left on device'],
    'Memory Warning': ['Memory usage exceeds 90%', 'App memory leaks detected'],
    'Connectivity Lost': ['Device offline for 2+ hours', 'WiFi disconnected'],
    'Security Alert': ['Suspicious app installed', 'Root/jailbreak detected'],
    'Policy Violation': ['Lockscreen policy not applied', 'Password requirements not met'],
    'OS Update Available': ['Android 14 update pending', 'Security patch available'],
    'App Malware Detected': ['Malware found in app', 'Suspicious permissions detected'],
    'Certificate Expired': ['Certificate expired or about to expire', 'SSL certificate invalid'],
    'Configuration Drift': ['Config differs from policy', 'Settings reverted by user']
  }

  const deviceNames = [
    'iPhone 14 Pro', 'Samsung Galaxy S23', 'Pixel 7', 'iPad Pro',
    'OnePlus 11', 'Moto G53', 'Samsung Tab S8', 'iPhone 15',
    'Galaxy A54', 'Pixel 6'
  ]

  const alerts: HealthAlert[] = []
  for (let i = 0; i < count; i++) {
    const alertType = alertTypes[Math.floor(Math.random() * alertTypes.length)]
    const typeMessages = messages[alertType]
    const severity = ['Critical', 'High', 'Medium', 'Low'][
      Math.floor(Math.random() * 4)
    ] as 'Critical' | 'High' | 'Medium' | 'Low'

    // 70% active, 30% resolved
    const isResolved = Math.random() < 0.3
    const createdAt = new Date(Date.now() - Math.random() * 7 * 24 * 60 * 60 * 1000)
    const resolvedAt = isResolved ? new Date(createdAt.getTime() + Math.random() * 24 * 60 * 60 * 1000) : null

    alerts.push({
      alertId: `alert-${String(i + 1).padStart(6, '0')}`,
      deviceId: `device-${String(Math.floor(Math.random() * 100) + 1).padStart(3, '0')}`,
      deviceName: deviceNames[Math.floor(Math.random() * deviceNames.length)],
      alertType,
      severity,
      type: alertType,
      message: typeMessages[Math.floor(Math.random() * typeMessages.length)],
      timestamp: createdAt.toISOString(),
      resolvedAt: resolvedAt?.toISOString() || null,
      resolved: !!resolvedAt
    })
  }

  return alerts
}
