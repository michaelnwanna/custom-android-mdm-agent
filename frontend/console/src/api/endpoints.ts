/**
 * Centralized API endpoint configuration
 * All endpoints should be defined here for consistency
 */

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5000'
const API_VERSION = '/api/v1'

export const API_ENDPOINTS = {
  // Authentication
  AUTH: {
    LOGIN: `${API_BASE_URL}${API_VERSION}/auth/login`,
    LOGOUT: `${API_BASE_URL}${API_VERSION}/auth/logout`,
    REFRESH: `${API_BASE_URL}${API_VERSION}/auth/refresh`,
    VERIFY: `${API_BASE_URL}${API_VERSION}/auth/verify`
  },

  // Devices
  DEVICES: {
    LIST: `${API_BASE_URL}${API_VERSION}/devices`,
    GET: (id: string) => `${API_BASE_URL}${API_VERSION}/devices/${id}`,
    CREATE: `${API_BASE_URL}${API_VERSION}/devices`,
    UPDATE: (id: string) => `${API_BASE_URL}${API_VERSION}/devices/${id}`,
    DELETE: (id: string) => `${API_BASE_URL}${API_VERSION}/devices/${id}`,
    TELEMETRY: (id: string) => `${API_BASE_URL}${API_VERSION}/devices/${id}/telemetry`,
    TAGS: {
      ASSIGN: `${API_BASE_URL}${API_VERSION}/devices/tags/assign`,
      REMOVE: `${API_BASE_URL}${API_VERSION}/devices/tags/remove`
    }
  },

  // Alerts
  ALERTS: {
    LIST: `${API_BASE_URL}${API_VERSION}/alerts`,
    GET: (id: string) => `${API_BASE_URL}${API_VERSION}/alerts/${id}`,
    RESOLVE: (id: string) => `${API_BASE_URL}${API_VERSION}/alerts/${id}/resolve`,
    BULK_RESOLVE: `${API_BASE_URL}${API_VERSION}/alerts/bulk/resolve`,
    UPDATE: (id: string) => `${API_BASE_URL}${API_VERSION}/alerts/${id}`
  },

  // Policies
  POLICIES: {
    LIST: `${API_BASE_URL}${API_VERSION}/policies`,
    GET: (id: string) => `${API_BASE_URL}${API_VERSION}/policies/${id}`,
    CREATE: `${API_BASE_URL}${API_VERSION}/policies`,
    UPDATE: (id: string) => `${API_BASE_URL}${API_VERSION}/policies/${id}`,
    DELETE: (id: string) => `${API_BASE_URL}${API_VERSION}/policies/${id}`,
    ACTIVATE: (id: string) => `${API_BASE_URL}${API_VERSION}/policies/${id}/activate`,
    DEACTIVATE: (id: string) => `${API_BASE_URL}${API_VERSION}/policies/${id}/deactivate`
  },

  // Releases
  RELEASES: {
    LIST: `${API_BASE_URL}${API_VERSION}/releases`,
    GET: (id: string) => `${API_BASE_URL}${API_VERSION}/releases/${id}`,
    CREATE: `${API_BASE_URL}${API_VERSION}/releases`,
    UPDATE: (id: string) => `${API_BASE_URL}${API_VERSION}/releases/${id}`,
    DELETE: (id: string) => `${API_BASE_URL}${API_VERSION}/releases/${id}`,
    PUBLISH: (id: string) => `${API_BASE_URL}${API_VERSION}/releases/${id}/publish`,
    DEPRECATE: (id: string) => `${API_BASE_URL}${API_VERSION}/releases/${id}/deprecate`
  },

  // Tags
  TAGS: {
    LIST: `${API_BASE_URL}${API_VERSION}/tags`,
    GET: (id: string) => `${API_BASE_URL}${API_VERSION}/tags/${id}`,
    CREATE: `${API_BASE_URL}${API_VERSION}/tags`,
    UPDATE: (id: string) => `${API_BASE_URL}${API_VERSION}/tags/${id}`,
    DELETE: (id: string) => `${API_BASE_URL}${API_VERSION}/tags/${id}`,
    ASSIGN: `${API_BASE_URL}${API_VERSION}/tags/assign`,
    UNASSIGN: `${API_BASE_URL}${API_VERSION}/tags/unassign`
  },

  // Settings
  SETTINGS: {
    LIST: `${API_BASE_URL}${API_VERSION}/settings`,
    GET: (id: string) => `${API_BASE_URL}${API_VERSION}/settings/${id}`,
    UPDATE: (id: string) => `${API_BASE_URL}${API_VERSION}/settings/${id}`,
    RESET: `${API_BASE_URL}${API_VERSION}/settings/reset`
  },

  // Fleet Metrics
  FLEET: {
    HEALTH: `${API_BASE_URL}${API_VERSION}/fleet/health`,
    AT_RISK: `${API_BASE_URL}${API_VERSION}/fleet/health/at-risk`
  },

  // Telemetry
  TELEMETRY: {
    LIST: `${API_BASE_URL}${API_VERSION}/telemetry`,
    GET: (deviceId: string) => `${API_BASE_URL}${API_VERSION}/telemetry/${deviceId}`
  }
}

// SignalR Hub URLs
export const SIGNALR_HUBS = {
  FLEET: `${API_BASE_URL}/hubs/fleet`,
  DEVICES: `${API_BASE_URL}/hubs/devices`,
  ALERTS: `${API_BASE_URL}/hubs/alerts`,
  POLICIES: `${API_BASE_URL}/hubs/policies`
}

export default API_ENDPOINTS
