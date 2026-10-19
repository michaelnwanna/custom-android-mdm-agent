// Device types
export interface Device {
  id?: string // For mock data
  deviceId: string
  enrollmentId: string
  name?: string // For mock data
  deviceName: string
  model: string
  osVersion: string
  status: 'Enrolled' | 'Suspended' | 'Decommissioned'
  lastSyncDate: string
  serialNumber: string
}

export interface DeviceTelemetry {
  deviceId: string
  timestamp: string
  batteryPercentage: number
  batteryStatus: 'Charging' | 'Discharging' | 'Unknown'
  storageUsageMB: number
  storageAvailableMB: number
  memoryUsageMB: number
  memoryAvailableMB: number
  cpuUsagePercentage: number
  wifiSignalStrength: number
  cellularSignalStrength: number
  connectivity: 'WiFi' | 'Cellular' | 'None'
}

export interface HealthAlert {
  id?: string // For mock data
  alertId?: string
  deviceId: string
  deviceName?: string // For mock data
  alertType?: string
  type?: string // For mock data
  severity: 'Critical' | 'High' | 'Medium' | 'Low'
  message: string
  createdAt?: string
  timestamp?: string // For mock data
  resolvedAt?: string | null
  resolved?: boolean // For mock data
}

// Fleet types
export interface FleetHealthMetrics {
  totalDevices: number
  enrolledDevices?: number // Alias for mock data
  onlineDevices?: number // For mock data
  offlineDevices: number
  lowBatteryDevices?: number
  lowStorageDevices?: number
  activeAlerts: number
  criticalAlerts: number
  averageBatteryPercentage?: number
  averageStorageUsagePercentage?: number
  devicesByStatus?: {
    healthy: number
    warning: number
    critical: number
    unknown: number
  }
  batteryDistribution?: {
    charging: number
    discharging: number
    critical: number
    unknown: number
  }
  storageDistribution?: Array<{
    deviceId: string
    name: string
    storageUsedPercent: number
  }>
  healthScore?: number
  lastUpdated?: string
}

// Policy types
export interface Policy {
  policyId: string
  name: string
  description: string
  createdAt: string
  updatedAt: string
  status: 'Active' | 'Draft' | 'Archived'
}

// Release types
export interface Release {
  releaseId: string
  packageName: string
  versionCode: number
  versionName: string
  releaseDate: string
  fileSize: number
  downloadUrl: string
  status: 'Draft' | 'Published' | 'Deprecated'
}

// Tag types
export interface Tag {
  tagId: string
  name: string
  description: string
  color: string
  deviceCount: number
}

// Pagination
export interface PaginatedResult<T> {
  items: T[]
  totalCount: number
  pageNumber: number
  pageSize: number
  hasNextPage: boolean
}

// API Response
export interface ApiResponse<T> {
  success: boolean
  data?: T
  errors?: string[]
}
