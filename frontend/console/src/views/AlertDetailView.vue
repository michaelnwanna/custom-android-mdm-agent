<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-3">
        <router-link
          to="/alerts"
          class="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300"
        >
          ← Back to Alerts
        </router-link>
        <span class="text-gray-400">/</span>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Alert Details</h1>
      </div>
      <button
        @click="refreshAlert"
        class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition flex items-center gap-2"
      >
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
        </svg>
        Refresh
      </button>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading alert details...</p>
    </div>

    <!-- Not Found State -->
    <div v-else-if="!alert" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">Alert not found</h3>
      <p class="text-gray-600 dark:text-gray-400">The alert you're looking for doesn't exist.</p>
    </div>

    <!-- Alert Details -->
    <div v-else class="space-y-6">
      <!-- Alert Summary Card -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div class="flex items-start justify-between mb-4">
          <div>
            <span :class="[
              'px-4 py-2 rounded-full text-sm font-bold inline-block mb-3',
              alert.severity === 'Critical' ? 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200' :
              alert.severity === 'High' ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200' :
              alert.severity === 'Medium' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
              'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
            ]">
              {{ alert.severity }} Severity
            </span>
            <h2 class="text-2xl font-bold text-gray-900 dark:text-white mt-2">
              {{ alert.alertType || alert.type || 'Alert' }}
            </h2>
          </div>
          <span :class="[
            'px-3 py-1 rounded-full text-sm font-medium',
            !alert.resolvedAt && !alert.resolved
              ? 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
              : 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
          ]">
            {{ !alert.resolvedAt && !alert.resolved ? 'Active' : 'Resolved' }}
          </span>
        </div>

        <p class="text-lg text-gray-700 dark:text-gray-300 mb-6">{{ alert.message }}</p>

        <!-- Quick Actions -->
        <div v-if="!alert.resolvedAt && !alert.resolved" class="flex gap-3">
          <button
            @click="resolveThisAlert"
            :disabled="resolving"
            class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition flex items-center gap-2"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
            </svg>
            {{ resolving ? 'Resolving...' : 'Resolve Alert' }}
          </button>
          <button
            @click="showConfirmDialog = true"
            class="px-4 py-2 bg-gray-300 dark:bg-gray-600 text-gray-900 dark:text-white rounded-lg hover:bg-gray-400 dark:hover:bg-gray-500 transition"
          >
            Dismiss
          </button>
        </div>
        <div v-else class="text-green-600 dark:text-green-400 font-medium flex items-center gap-2">
          <svg class="w-5 h-5" fill="currentColor" viewBox="0 0 20 20">
            <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd" />
          </svg>
          Resolved on {{ formatDate(alert.resolvedAt) }}
        </div>
      </div>

      <!-- Alert Information Grid -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <!-- Alert Metadata -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Alert Information</h3>
          <div class="space-y-4">
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Alert ID</label>
              <p class="text-gray-900 dark:text-white font-mono text-sm mt-1">{{ alert.alertId }}</p>
            </div>
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Type</label>
              <p class="text-gray-900 dark:text-white mt-1">{{ alert.alertType || alert.type || 'General' }}</p>
            </div>
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Severity</label>
              <p class="text-gray-900 dark:text-white mt-1">{{ alert.severity }}</p>
            </div>
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Created</label>
              <p class="text-gray-900 dark:text-white mt-1">{{ formatDateTime(alert.createdAt || alert.timestamp) }}</p>
              <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">{{ formatTimeAgo(alert.createdAt || alert.timestamp) }}</p>
            </div>
            <div v-if="alert.resolvedAt">
              <label class="text-sm text-gray-600 dark:text-gray-400">Resolved</label>
              <p class="text-gray-900 dark:text-white mt-1">{{ formatDateTime(alert.resolvedAt) }}</p>
            </div>
          </div>
        </div>

        <!-- Device Information -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Affected Device</h3>
          <div v-if="device" class="space-y-4">
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Device Name</label>
              <p class="text-gray-900 dark:text-white mt-1">{{ device.deviceName || device.name }}</p>
            </div>
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Device ID</label>
              <p class="text-gray-900 dark:text-white font-mono text-sm mt-1">{{ device.deviceId || device.id }}</p>
            </div>
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Model</label>
              <p class="text-gray-900 dark:text-white mt-1">{{ device.model }}</p>
            </div>
            <div>
              <label class="text-sm text-gray-600 dark:text-gray-400">Status</label>
              <span :class="[
                'px-2 py-1 rounded text-xs font-medium inline-block mt-1',
                device.status === 'Enrolled' ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200' :
                device.status === 'Suspended' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
                'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200'
              ]">
                {{ device.status }}
              </span>
            </div>
            <router-link
              :to="`/devices/${device.deviceId || device.id}`"
              class="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300 text-sm font-medium inline-flex items-center gap-1 mt-2"
            >
              View Device Details
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
              </svg>
            </router-link>
          </div>
          <div v-else class="text-gray-600 dark:text-gray-400">
            Device information not available
          </div>
        </div>
      </div>

      <!-- Alert Message & Context -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Alert Details</h3>
        <div class="bg-gray-50 dark:bg-gray-700/50 rounded p-4">
          <p class="text-gray-700 dark:text-gray-300 text-base leading-relaxed">
            {{ alert.message }}
          </p>
        </div>

        <div v-if="alert.severity === 'Critical'" class="mt-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
          <h4 class="font-semibold text-red-900 dark:text-red-200 mb-2">Recommended Actions</h4>
          <ul class="list-disc list-inside text-red-800 dark:text-red-300 text-sm space-y-1">
            <li>Immediately investigate and resolve this critical issue</li>
            <li>Contact the device user if manual intervention is needed</li>
            <li>Consider isolating the device from the network if necessary</li>
            <li>Review device logs for additional context</li>
          </ul>
        </div>
        <div v-else-if="alert.severity === 'High'" class="mt-4 bg-orange-50 dark:bg-orange-900/20 border border-orange-200 dark:border-orange-800 rounded-lg p-4">
          <h4 class="font-semibold text-orange-900 dark:text-orange-200 mb-2">Recommended Actions</h4>
          <ul class="list-disc list-inside text-orange-800 dark:text-orange-300 text-sm space-y-1">
            <li>Prioritize this issue for investigation</li>
            <li>Monitor the device closely for further issues</li>
            <li>Consider reaching out to the device user</li>
          </ul>
        </div>
        <div v-else class="mt-4 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4">
          <h4 class="font-semibold text-blue-900 dark:text-blue-200 mb-2">Note</h4>
          <p class="text-blue-800 dark:text-blue-300 text-sm">
            Monitor this alert and take appropriate action based on your organization's policies.
          </p>
        </div>
      </div>

      <!-- Related Alerts -->
      <div v-if="relatedAlerts.length > 0" class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Related Alerts</h3>
        <div class="space-y-3">
          <router-link
            v-for="relatedAlert in relatedAlerts.slice(0, 5)"
            :key="relatedAlert.alertId"
            :to="`/alerts/${relatedAlert.alertId}`"
            class="block p-3 border border-gray-200 dark:border-gray-700 rounded hover:bg-gray-50 dark:hover:bg-gray-700/50 transition"
          >
            <div class="flex items-center justify-between">
              <div>
                <p class="font-medium text-gray-900 dark:text-white">{{ relatedAlert.alertType || 'Alert' }}</p>
                <p class="text-sm text-gray-600 dark:text-gray-400">{{ formatTimeAgo(relatedAlert.createdAt || relatedAlert.timestamp) }}</p>
              </div>
              <span :class="[
                'px-2 py-1 rounded text-xs font-medium',
                relatedAlert.severity === 'Critical' ? 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200' :
                relatedAlert.severity === 'High' ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200' :
                relatedAlert.severity === 'Medium' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
                'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
              ]">
                {{ relatedAlert.severity }}
              </span>
            </div>
          </router-link>
        </div>
      </div>
    </div>

    <!-- Confirm Dismiss Dialog -->
    <div v-if="showConfirmDialog" class="fixed inset-0 bg-black/50 dark:bg-black/70 flex items-center justify-center z-50">
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg p-6 max-w-sm">
        <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-2">Dismiss Alert</h3>
        <p class="text-gray-600 dark:text-gray-400 mb-6">
          Are you sure you want to dismiss this alert? It will not reappear unless the issue occurs again.
        </p>
        <div class="flex gap-3 justify-end">
          <button
            @click="showConfirmDialog = false"
            class="px-4 py-2 bg-gray-300 dark:bg-gray-600 text-gray-900 dark:text-white rounded-lg hover:bg-gray-400 dark:hover:bg-gray-500 transition"
          >
            Cancel
          </button>
          <button
            @click="dismissAlert"
            :disabled="resolving"
            class="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:bg-gray-400 transition"
          >
            {{ resolving ? 'Dismissing...' : 'Dismiss' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAlertStore } from '@stores/alertStore'
import { useDeviceStore } from '@stores/deviceStore'
import type { HealthAlert, Device } from '@types'

const route = useRoute()
const router = useRouter()
const alertStore = useAlertStore()
const deviceStore = useDeviceStore()

const loading = ref(false)
const resolving = ref(false)
const showConfirmDialog = ref(false)

const alertId = computed(() => route.params.id as string)

const alert = computed(() => alertStore.getAlertById(alertId.value))

const device = computed(() => {
  if (!alert.value) return undefined
  return deviceStore.devices.find(d => (d.deviceId || d.id) === alert.value?.deviceId)
})

const relatedAlerts = computed(() => {
  if (!alert.value) return []
  return alertStore.alerts.filter(a =>
    a.alertId !== alert.value?.alertId &&
    (a.deviceId === alert.value?.deviceId || a.alertType === alert.value?.alertType)
  )
})

function formatDate(timestamp?: string): string {
  if (!timestamp) return 'Unknown'
  return new Date(timestamp).toLocaleDateString()
}

function formatDateTime(timestamp?: string): string {
  if (!timestamp) return 'Unknown'
  return new Date(timestamp).toLocaleString()
}

function formatTimeAgo(timestamp?: string): string {
  if (!timestamp) return 'Unknown'
  const date = new Date(timestamp)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  const diffHours = Math.floor(diffMs / 3600000)
  const diffDays = Math.floor(diffMs / 86400000)

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins}m ago`
  if (diffHours < 24) return `${diffHours}h ago`
  if (diffDays < 30) return `${diffDays}d ago`
  return 'More than a month ago'
}

async function resolveThisAlert() {
  if (!alert.value) return
  resolving.value = true
  try {
    await alertStore.resolveAlert(alert.value.alertId || '')
  } finally {
    resolving.value = false
  }
}

async function dismissAlert() {
  if (!alert.value) return
  resolving.value = true
  try {
    await alertStore.resolveAlert(alert.value.alertId || '')
    showConfirmDialog.value = false
  } finally {
    resolving.value = false
  }
}

function refreshAlert() {
  if (!alert.value) return
  alertStore.fetchAlerts()
}

onMounted(() => {
  if (!alertStore.alerts.length) {
    loading.value = true
    alertStore.fetchAlerts().then(() => {
      loading.value = false
    })
  }
  if (!deviceStore.devices.length) {
    deviceStore.fetchDevices()
  }
})
</script>