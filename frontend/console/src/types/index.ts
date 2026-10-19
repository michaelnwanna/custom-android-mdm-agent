// Device types
export interface Device {
  deviceId: string
  enrollmentId: string
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
  alertId: string
  deviceId: string
  alertType: string
  severity: 'Critical' | 'High' | 'Medium' | 'Low'
  message: string
  createdAt: string
  resolvedAt: string | null
}

// Fleet types
export interface FleetHealthMetrics {
  totalDevices: number
  enrolledDevices: number
  offlineDevices: number
  lowBatteryDevices: number
  lowStorageDevices: number
  activeAlerts: number
  criticalAlerts: number
  averageBatteryPercentage: number
  averageStorageUsagePercentage: number
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
