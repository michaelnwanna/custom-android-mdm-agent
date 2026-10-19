/**
 * Device API wrapper for unified device operations
 */

import { apiClient } from './client'
import { API_ENDPOINTS } from './endpoints'
import type { Device, DeviceTelemetry, PaginatedResult } from '@/types'

export interface DeviceQueryParams {
  pageNumber?: number
  pageSize?: number
  status?: string
  search?: string
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
}

export interface DeviceCommand {
  type: 'lock' | 'wipe' | 'restart' | 'updatePolicy' | 'executeCommand'
  deviceId: string
  parameters?: Record<string, unknown>
}

export interface CommandResult {
  id: string
  deviceId: string
  command: string
  status: 'Pending' | 'Success' | 'Failed'
  startTime: string
  endTime?: string
  result?: string
}

/**
 * Fetch paginated list of devices
 */
export async function fetchDevices(
  params?: DeviceQueryParams
): Promise<PaginatedResult<Device>> {
  const queryParams = {
    pageNumber: params?.pageNumber || 1,
    pageSize: params?.pageSize || 20,
    ...(params?.status && { status: params.status }),
    ...(params?.search && { search: params.search }),
    ...(params?.sortBy && { sortBy: params.sortBy }),
    ...(params?.sortOrder && { sortOrder: params.sortOrder })
  }

  return apiClient.get<PaginatedResult<Device>>(API_ENDPOINTS.DEVICES.LIST, {
    params: queryParams
  })
}

/**
 * Fetch a single device by ID
 */
export async function fetchDevice(deviceId: string): Promise<Device> {
  return apiClient.get<Device>(API_ENDPOINTS.DEVICES.GET(deviceId))
}

/**
 * Fetch device telemetry
 */
export async function fetchDeviceTelemetry(deviceId: string): Promise<DeviceTelemetry> {
  return apiClient.get<DeviceTelemetry>(API_ENDPOINTS.DEVICES.TELEMETRY(deviceId))
}

/**
 * Create a new device
 */
export async function createDevice(data: Omit<Device, 'id'>): Promise<Device> {
  return apiClient.post<Device>(API_ENDPOINTS.DEVICES.CREATE, data)
}

/**
 * Update device information
 */
export async function updateDevice(deviceId: string, data: Partial<Device>): Promise<Device> {
  return apiClient.put<Device>(API_ENDPOINTS.DEVICES.UPDATE(deviceId), data)
}

/**
 * Delete a device
 */
export async function deleteDevice(deviceId: string): Promise<void> {
  await apiClient.delete(API_ENDPOINTS.DEVICES.DELETE(deviceId))
}

/**
 * Assign tags to device(s)
 */
export async function assignTagsToDevices(
  deviceIds: string[],
  tagIds: string[]
): Promise<void> {
  await apiClient.post(API_ENDPOINTS.DEVICES.TAGS.ASSIGN, {
    deviceIds,
    tagIds
  })
}

/**
 * Remove tags from device(s)
 */
export async function removeTagsFromDevices(
  deviceIds: string[],
  tagIds: string[]
): Promise<void> {
  await apiClient.post(API_ENDPOINTS.DEVICES.TAGS.REMOVE, {
    deviceIds,
    tagIds
  })
}

/**
 * Execute a command on a device
 */
export async function executeDeviceCommand(command: DeviceCommand): Promise<CommandResult> {
  const endpoint = `${API_ENDPOINTS.DEVICES.GET(command.deviceId)}/commands`
  return apiClient.post<CommandResult>(endpoint, {
    type: command.type,
    parameters: command.parameters
  })
}

/**
 * Lock a device
 */
export async function lockDevice(deviceId: string): Promise<CommandResult> {
  return executeDeviceCommand({
    type: 'lock',
    deviceId
  })
}

/**
 * Wipe a device (factory reset)
 */
export async function wipeDevice(deviceId: string): Promise<CommandResult> {
  return executeDeviceCommand({
    type: 'wipe',
    deviceId
  })
}

/**
 * Restart a device
 */
export async function restartDevice(deviceId: string): Promise<CommandResult> {
  return executeDeviceCommand({
    type: 'restart',
    deviceId
  })
}

/**
 * Update device policy
 */
export async function updateDevicePolicy(deviceId: string, policyId: string): Promise<CommandResult> {
  return executeDeviceCommand({
    type: 'updatePolicy',
    deviceId,
    parameters: { policyId }
  })
}

/**
 * Get device command status
 */
export async function getCommandStatus(deviceId: string, commandId: string): Promise<CommandResult> {
  const endpoint = `${API_ENDPOINTS.DEVICES.GET(deviceId)}/commands/${commandId}`
  return apiClient.get<CommandResult>(endpoint)
}

/**
 * Get device command history
 */
export async function getCommandHistory(
  deviceId: string,
  limit: number = 50
): Promise<CommandResult[]> {
  const endpoint = `${API_ENDPOINTS.DEVICES.GET(deviceId)}/commands`
  return apiClient.get<CommandResult[]>(endpoint, {
    params: { limit }
  })
}
