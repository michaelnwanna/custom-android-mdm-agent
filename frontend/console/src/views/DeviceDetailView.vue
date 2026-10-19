<template>
  <div class="space-y-6">
    <!-- Page header -->
    <div class="flex items-center justify-between">
      <div>
        <router-link to="/devices" class="text-primary hover:underline text-sm mb-2">← Back to Devices</router-link>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white mt-2">Device Details</h1>
      </div>
      <div class="flex gap-2">
        <button
          @click="refreshDevice"
          class="px-4 py-2 bg-gray-500 text-white rounded-lg hover:bg-gray-600 transition-colors flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
          </svg>
          Refresh
        </button>
        <button
          @click="editMode = !editMode"
          :class="[
            'px-4 py-2 rounded-lg transition-colors flex items-center gap-2',
            editMode
              ? 'bg-gray-500 text-white hover:bg-gray-600'
              : 'bg-primary text-white hover:bg-blue-600'
          ]"
        >
          {{ editMode ? 'Cancel' : 'Edit' }}
        </button>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-8 text-center">
      <div class="inline-block">
        <div class="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin" />
      </div>
      <p class="text-gray-600 dark:text-gray-400 mt-4">Loading device...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="bg-white dark:bg-gray-800 rounded-lg shadow p-8 text-center text-red-600 dark:text-red-400">
      <p class="font-medium">{{ error }}</p>
      <button
        @click="refreshDevice"
        class="mt-4 px-4 py-2 bg-primary text-white rounded hover:bg-blue-600 transition-colors"
      >
        Try Again
      </button>
    </div>

    <!-- Device Content -->
    <template v-else-if="device">
      <!-- Device Info Section -->
      <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <!-- Main Info -->
        <div class="lg:col-span-2 space-y-6">
          <!-- Basic Information -->
          <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
            <h2 class="text-xl font-bold text-gray-900 dark:text-white mb-4">Basic Information</h2>
            <div class="space-y-4">
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Device Name</label>
                  <input
                    v-if="editMode"
                    v-model="editedDevice.deviceName"
                    type="text"
                    class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg dark:bg-gray-700 dark:text-white"
                  />
                  <div v-else class="text-gray-900 dark:text-white font-medium">{{ device.deviceName }}</div>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Serial Number</label>
                  <div class="text-gray-900 dark:text-white">{{ device.serialNumber }}</div>
                </div>
              </div>

              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Model</label>
                  <div class="text-gray-900 dark:text-white">{{ device.model }}</div>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">OS Version</label>
                  <div class="text-gray-900 dark:text-white">{{ device.osVersion }}</div>
                </div>
              </div>

              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Enrollment ID</label>
                  <div class="text-gray-900 dark:text-white font-mono text-sm">{{ device.enrollmentId }}</div>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Status</label>
                  <span
                    :class="[
                      'inline-block px-3 py-1 rounded-full text-sm font-semibold',
                      statusBadgeClass(device.status)
                    ]"
                  >
                    {{ device.status }}
                  </span>
                </div>
              </div>

              <div>
                <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Last Sync</label>
                <div class="text-gray-900 dark:text-white">{{ formatDate(device.lastSyncDate) }}</div>
              </div>

              <div v-if="editMode" class="pt-4 border-t border-gray-200 dark:border-gray-700 flex gap-2">
                <button
                  @click="saveChanges"
                  class="px-4 py-2 bg-primary text-white rounded hover:bg-blue-600 transition-colors"
                >
                  Save Changes
                </button>
                <button
                  @click="editMode = false; resetEdit()"
                  class="px-4 py-2 bg-gray-400 text-white rounded hover:bg-gray-500 transition-colors"
                >
                  Cancel
                </button>
              </div>
            </div>
          </div>

          <!-- Device Telemetry -->
          <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
            <h2 class="text-xl font-bold text-gray-900 dark:text-white mb-4">Device Telemetry</h2>
            <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div class="p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
                <div class="text-sm text-gray-600 dark:text-gray-400 mb-1">Battery</div>
                <div class="text-2xl font-bold text-gray-900 dark:text-white">{{ telemetry?.batteryPercentage || 'N/A' }}%</div>
              </div>
              <div class="p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
                <div class="text-sm text-gray-600 dark:text-gray-400 mb-1">Storage Used</div>
                <div class="text-2xl font-bold text-gray-900 dark:text-white">
                  {{ telemetry?.storageUsageMB ? formatStorage(telemetry.storageUsageMB) : 'N/A' }}
                </div>
              </div>
              <div class="p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
                <div class="text-sm text-gray-600 dark:text-gray-400 mb-1">Memory Used</div>
                <div class="text-2xl font-bold text-gray-900 dark:text-white">
                  {{ telemetry?.memoryUsageMB ? formatStorage(telemetry.memoryUsageMB) : 'N/A' }}
                </div>
              </div>
              <div class="p-4 bg-gray-50 dark:bg-gray-700 rounded-lg">
                <div class="text-sm text-gray-600 dark:text-gray-400 mb-1">CPU Usage</div>
                <div class="text-2xl font-bold text-gray-900 dark:text-white">{{ telemetry?.cpuUsagePercentage || 'N/A' }}%</div>
              </div>
            </div>
          </div>
        </div>

        <!-- Sidebar Actions -->
        <div class="space-y-6">
          <!-- Quick Actions -->
          <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
            <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Actions</h3>
            <div class="space-y-2">
              <button
                @click="restartDevice"
                :disabled="executing"
                class="w-full px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600 transition-colors text-sm disabled:opacity-50"
              >
                {{ executing ? 'Executing...' : 'Restart Device' }}
              </button>
              <button
                @click="lockDevice"
                :disabled="executing"
                class="w-full px-4 py-2 bg-orange-500 text-white rounded hover:bg-orange-600 transition-colors text-sm disabled:opacity-50"
              >
                {{ executing ? 'Executing...' : 'Lock Device' }}
              </button>
              <button
                @click="wipeDevice"
                :disabled="executing"
                class="w-full px-4 py-2 bg-red-600 text-white rounded hover:bg-red-700 transition-colors text-sm disabled:opacity-50"
              >
                {{ executing ? 'Executing...' : 'Wipe Device' }}
              </button>
              <button
                @click="suspendDevice"
                :disabled="executing"
                class="w-full px-4 py-2 bg-yellow-500 text-white rounded hover:bg-yellow-600 transition-colors text-sm disabled:opacity-50"
              >
                {{ executing ? 'Executing...' : 'Suspend' }}
              </button>
              <button
                @click="decommissionDevice"
                :disabled="executing"
                class="w-full px-4 py-2 bg-red-500 text-white rounded hover:bg-red-600 transition-colors text-sm disabled:opacity-50"
              >
                {{ executing ? 'Executing...' : 'Decommission' }}
              </button>
            </div>
          </div>

          <!-- Device Info Card -->
          <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
            <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Device ID</h3>
            <div class="font-mono text-xs text-gray-600 dark:text-gray-400 break-all bg-gray-50 dark:bg-gray-700 p-3 rounded">
              {{ device.deviceId || 'N/A' }}
            </div>
          </div>

          <!-- Alerts -->
          <div v-if="deviceAlerts.length > 0" class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
            <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Active Alerts ({{ deviceAlerts.length }})</h3>
            <div class="space-y-2 max-h-64 overflow-y-auto">
              <div
                v-for="alert in deviceAlerts"
                :key="alert.alertId || alert.id"
                :class="[
                  'p-3 rounded text-xs',
                  alert.severity === 'Critical'
                    ? 'bg-red-100 dark:bg-red-900 text-red-800 dark:text-red-200'
                    : 'bg-yellow-100 dark:bg-yellow-900 text-yellow-800 dark:text-yellow-200'
                ]"
              >
                {{ alert.message }}
              </div>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDeviceStore } from '@stores/deviceStore'
import * as deviceApi from '@/api/deviceApi'
import toastService from '@services/toastService'
import type { Device, DeviceTelemetry } from '@types'

const route = useRoute()
const router = useRouter()
const deviceStore = useDeviceStore()

// State
const editMode = ref(false)
const editedDevice = ref<Partial<Device>>({})
const telemetry = ref<DeviceTelemetry | null>(null)
const deviceAlerts = ref<any[]>([])
const commandHistory = ref<any[]>([])
const executing = ref(false)

// Computed
const deviceId = computed(() => route.params.id as string)
const device = computed(() => deviceStore.selectedDevice)
const loading = computed(() => deviceStore.loading)
const error = computed(() => deviceStore.error)

// Methods
const refreshDevice = async () => {
  try {
    await deviceStore.fetchDeviceById(deviceId.value)
    await fetchTelemetry()
    await fetchCommandHistory()
    resetEdit()
  } catch (err) {
    console.error('Failed to refresh device:', err)
  }
}

const fetchTelemetry = async () => {
  try {
    telemetry.value = await deviceApi.fetchDeviceTelemetry(deviceId.value)
  } catch (err) {
    console.error('Failed to fetch telemetry:', err)
  }
}

const fetchCommandHistory = async () => {
  try {
    commandHistory.value = await deviceApi.getCommandHistory(deviceId.value, 20)
  } catch (err) {
    console.error('Failed to fetch command history:', err)
  }
}

const resetEdit = () => {
  if (device.value) {
    editedDevice.value = { ...device.value }
  }
}

const saveChanges = async () => {
  try {
    if (!device.value?.deviceId) return
    await deviceStore.updateDevice({
      ...device.value,
      ...editedDevice.value
    })
    toastService.success('Device updated successfully')
    editMode.value = false
    await refreshDevice()
  } catch (err) {
    toastService.error('Failed to update device')
  }
}

const lockDevice = async () => {
  if (!confirm('Lock this device? The user will not be able to use it until unlocked.')) return
  
  executing.value = true
  try {
    await deviceStore.lockDevice(deviceId.value)
    toastService.success('Lock command sent to device')
    await fetchCommandHistory()
  } catch (err) {
    toastService.error('Failed to lock device')
  } finally {
    executing.value = false
  }
}

const wipeDevice = async () => {
  if (!confirm('Wipe this device? This will perform a factory reset and cannot be undone.')) return
  
  executing.value = true
  try {
    await deviceStore.wipeDevice(deviceId.value)
    toastService.success('Wipe command sent to device')
    await fetchCommandHistory()
    await refreshDevice()
  } catch (err) {
    toastService.error('Failed to wipe device')
  } finally {
    executing.value = false
  }
}

const restartDevice = async () => {
  if (!confirm('Restart this device?')) return
  
  executing.value = true
  try {
    await deviceStore.restartDevice(deviceId.value)
    toastService.success('Restart command sent to device')
    await fetchCommandHistory()
  } catch (err) {
    toastService.error('Failed to restart device')
  } finally {
    executing.value = false
  }
}

const suspendDevice = async () => {
  if (!confirm('Suspend this device?')) return
  
  executing.value = true
  try {
    await deviceStore.lockDevice(deviceId.value)
    toastService.success('Device suspended')
    await refreshDevice()
  } catch (err) {
    toastService.error('Failed to suspend device')
  } finally {
    executing.value = false
  }
}

const decommissionDevice = async () => {
  if (!confirm('Decommission this device? This cannot be undone.')) return
  
  executing.value = true
  try {
    if (device.value?.deviceId) {
      await deviceStore.deleteDevice(device.value.deviceId)
    }
    toastService.success('Device decommissioned')
    await router.push('/devices')
  } catch (err) {
    toastService.error('Failed to decommission device')
  } finally {
    executing.value = false
  }
}

const statusBadgeClass = (status: string): string => {
  switch (status) {
    case 'Enrolled':
      return 'bg-green-100 dark:bg-green-900 text-green-800 dark:text-green-200'
    case 'Suspended':
      return 'bg-yellow-100 dark:bg-yellow-900 text-yellow-800 dark:text-yellow-200'
    case 'Decommissioned':
      return 'bg-red-100 dark:bg-red-900 text-red-800 dark:text-red-200'
    default:
      return 'bg-gray-100 dark:bg-gray-900 text-gray-800 dark:text-gray-200'
  }
}

const formatDate = (dateString: string): string => {
  const date = new Date(dateString)
  const now = new Date()
  const diff = now.getTime() - date.getTime()
  const minutes = Math.floor(diff / 60000)

  if (minutes < 1) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 30) return `${days}d ago`
  
  return date.toLocaleDateString()
}

const formatStorage = (mb: number): string => {
  if (mb < 1024) return `${mb.toFixed(0)} MB`
  return `${(mb / 1024).toFixed(1)} GB`
}

// Lifecycle
onMounted(async () => {
  await refreshDevice()
})
</script>
