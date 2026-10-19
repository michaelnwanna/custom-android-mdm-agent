<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Alerts</h1>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1">
          {{ activeAlerts.length }} active · {{ criticalAlerts.length }} critical · {{ displayText }}
        </p>
      </div>
      <button
        @click="refreshAlerts"
        class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 transition flex items-center gap-2"
        :disabled="loading"
      >
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
        </svg>
        {{ loading ? 'Refreshing...' : 'Refresh' }}
      </button>
    </div>

    <!-- Filters & Search -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
      <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
        <!-- Search -->
        <div class="md:col-span-2">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Search</label>
          <input
            :value="search"
            @input="updateSearch($event.target.value)"
            type="text"
            placeholder="Search by device name or message..."
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Status Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Status</label>
          <select
            :value="status"
            @change="updateStatus($event.target.value)"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="">All Statuses</option>
            <option value="Open">Open</option>
            <option value="Resolved">Resolved</option>
            <option value="Acknowledged">Acknowledged</option>
          </select>
        </div>

        <!-- Severity Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Severity</label>
          <select
            :value="severity"
            @change="updateSeverity($event.target.value)"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="">All Levels</option>
            <option value="Critical">Critical</option>
            <option value="High">High</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>
        </div>
      </div>

      <!-- Clear Filters & Bulk Actions -->
      <div class="flex items-center justify-between border-t border-gray-200 dark:border-gray-700 pt-4">
        <div class="flex gap-2">
          <button
            v-if="isFilterActive"
            @click="clearFilters"
            class="px-3 py-1 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white"
          >
            Clear Filters
          </button>
        </div>
        <div v-if="selectedAlerts.length > 0" class="flex items-center gap-2">
          <span class="text-sm text-gray-600 dark:text-gray-400">{{ selectedAlerts.length }} selected</span>
          <button
            @click="handleResolveBulk"
            :disabled="resolvingBulk"
            class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition flex items-center gap-2 text-sm"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
            </svg>
            {{ resolvingBulk ? 'Resolving...' : 'Resolve Selected' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading alerts...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
      <div class="flex items-start gap-3">
        <svg class="w-5 h-5 text-red-600 dark:text-red-400 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
        </svg>
        <div>
          <h3 class="font-medium text-red-900 dark:text-red-200">Error loading alerts</h3>
          <p class="text-sm text-red-700 dark:text-red-300 mt-1">{{ error }}</p>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <div v-else-if="alerts.length === 0" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">No alerts found</h3>
      <p class="text-gray-600 dark:text-gray-400">{{ isFilterActive ? 'Try adjusting your filters' : 'Great! No active alerts right now' }}</p>
    </div>

    <!-- Alerts Table -->
    <div v-else class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
      <table class="w-full">
        <thead class="bg-gray-50 dark:bg-gray-700 border-b border-gray-200 dark:border-gray-600">
          <tr>
            <th class="px-6 py-3 text-left">
              <input type="checkbox" :checked="selectedAlerts.length > 0 && selectedAlerts.length === alerts.length" @change="toggleSelectAll" class="rounded" />
            </th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Severity</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Device / Message</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Type</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Created</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Status</th>
            <th class="px-6 py-3 text-right text-sm font-medium text-gray-700 dark:text-gray-300">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-200 dark:divide-gray-700">
          <tr v-for="alert in alerts" :key="alert.alertId" class="hover:bg-gray-50 dark:hover:bg-gray-700/50 transition">
            <td class="px-6 py-4">
              <input type="checkbox" :checked="selectedAlerts.includes(alert.alertId)" @change="toggleAlert(alert.alertId)" class="rounded" />
            </td>
            <td class="px-6 py-4">
              <span :class="['px-3 py-1 rounded-full text-xs font-medium', severityBadgeClass(alert.severity)]">{{ alert.severity }}</span>
            </td>
            <td class="px-6 py-4">
              <div class="flex flex-col">
                <span class="font-medium text-gray-900 dark:text-white">{{ alert.deviceName || 'Unknown Device' }}</span>
                <span class="text-sm text-gray-600 dark:text-gray-400 truncate">{{ alert.message }}</span>
              </div>
            </td>
            <td class="px-6 py-4 text-sm text-gray-600 dark:text-gray-400">{{ alert.type || alert.alertType || 'General' }}</td>
            <td class="px-6 py-4 text-sm text-gray-600 dark:text-gray-400">{{ formatTime(alert.timestamp || alert.createdAt) }}</td>
            <td class="px-6 py-4">
              <span :class="['px-2 py-1 rounded text-xs font-medium', !alert.resolved && !alert.resolvedAt ? 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200' : 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300']">
                {{ !alert.resolved && !alert.resolvedAt ? 'Active' : 'Resolved' }}
              </span>
            </td>
            <td class="px-6 py-4 text-right">
              <div class="flex justify-end gap-2">
                <router-link :to="`/alerts/${alert.alertId}`" class="text-blue-600 dark:text-blue-400 hover:text-blue-800 text-sm font-medium">View</router-link>
                <button
                  v-if="!alert.resolved && !alert.resolvedAt"
                  @click="handleResolveAlert(alert.alertId)"
                  :disabled="resolvingId === alert.alertId"
                  class="text-green-600 dark:text-green-400 hover:text-green-800 text-sm font-medium disabled:opacity-50"
                >
                  {{ resolvingId === alert.alertId ? 'Resolving...' : 'Resolve' }}
                </button>
                <button
                  v-else
                  @click="handleReopenAlert(alert.alertId)"
                  :disabled="resolvingId === alert.alertId"
                  class="text-orange-600 dark:text-orange-400 hover:text-orange-800 text-sm font-medium disabled:opacity-50"
                >
                  Reopen
                </button>
                <button
                  @click="handleDeleteAlert(alert.alertId)"
                  class="text-red-600 dark:text-red-400 hover:text-red-800 text-sm font-medium"
                >
                  Delete
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Pagination -->
    <div v-if="alerts.length > 0 && totalPages > 1" class="flex items-center justify-between bg-white dark:bg-gray-800 rounded-lg shadow p-4">
      <button
        @click="prevPage"
        :disabled="pageNumber === 1"
        class="px-3 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 transition"
      >
        Previous
      </button>
      <div class="flex gap-1">
        <button
          v-for="page in pageRange"
          :key="page"
          @click="goToPage(typeof page === 'number' ? page : pageNumber)"
          :disabled="typeof page === 'string'"
          :class="[
            'px-3 py-1 text-sm rounded transition',
            page === pageNumber
              ? 'bg-blue-600 text-white'
              : 'border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700',
            typeof page === 'string' ? 'cursor-default' : 'cursor-pointer'
          ]"
        >
          {{ page }}
        </button>
      </div>
      <button
        @click="nextPage"
        :disabled="pageNumber === totalPages"
        class="px-3 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 transition"
      >
        Next
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAlerts } from '@/composables/useAlerts'
import toastService from '@services/toastService'
import signalRService from '@services/signalRService'
import type { HealthAlert } from '@types'

const router = useRouter()

const {
  alerts,
  loading,
  error,
  severity,
  status,
  search,
  pageNumber,
  totalPages,
  pageRange,
  displayText,
  fetchAlerts,
  goToPage,
  nextPage,
  prevPage,
  updateSearch,
  updateSeverity,
  updateStatus,
  clearFilters,
  resolveAlert,
  reopenAlert,
  deleteAlert,
  bulkResolveAlerts,
  addAlert
} = useAlerts({ pageSize: 20, autoLoad: true })

const selectedAlerts = ref<string[]>([])
const resolvingId = ref<string | null>(null)
const resolvingBulk = ref(false)

const activeAlerts = computed(() => alerts.value.filter(a => !a.resolvedAt && !a.resolved))
const criticalAlerts = computed(() => activeAlerts.value.filter(a => a.severity === 'Critical'))
const isFilterActive = computed(() => search.value || severity.value || status.value)

const refreshAlerts = async () => {
  await fetchAlerts()
}

const handleResolveAlert = async (alertId: string) => {
  resolvingId.value = alertId
  try {
    const success = await resolveAlert(alertId)
    if (success) {
      toastService.success('Alert resolved')
    } else {
      toastService.error('Failed to resolve alert')
    }
  } finally {
    resolvingId.value = null
  }
}

const handleReopenAlert = async (alertId: string) => {
  resolvingId.value = alertId
  try {
    const success = await reopenAlert(alertId)
    if (success) {
      toastService.success('Alert reopened')
    } else {
      toastService.error('Failed to reopen alert')
    }
  } finally {
    resolvingId.value = null
  }
}

const handleDeleteAlert = async (alertId: string) => {
  if (!confirm('Delete this alert?')) return
  try {
    const success = await deleteAlert(alertId)
    if (success) {
      toastService.success('Alert deleted')
      selectedAlerts.value = selectedAlerts.value.filter(a => a !== alertId)
    } else {
      toastService.error('Failed to delete alert')
    }
  } catch (err) {
    toastService.error('Delete operation failed')
  }
}

const handleResolveBulk = async () => {
  if (selectedAlerts.value.length === 0) {
    toastService.warning('No alerts selected')
    return
  }
  if (!confirm(`Resolve ${selectedAlerts.value.length} alert(s)?`)) return
  
  resolvingBulk.value = true
  try {
    const success = await bulkResolveAlerts(selectedAlerts.value)
    if (success) {
      toastService.success(`${selectedAlerts.value.length} alert(s) resolved`)
      selectedAlerts.value = []
    } else {
      toastService.error('Failed to resolve alerts')
    }
  } finally {
    resolvingBulk.value = false
  }
}

const toggleAlert = (alertId: string) => {
  const index = selectedAlerts.value.indexOf(alertId)
  if (index > -1) {
    selectedAlerts.value.splice(index, 1)
  } else {
    selectedAlerts.value.push(alertId)
  }
}

const toggleSelectAll = () => {
  if (selectedAlerts.value.length === alerts.value.length) {
    selectedAlerts.value = []
  } else {
    selectedAlerts.value = alerts.value.map(a => a.alertId)
  }
}

const severityBadgeClass = (sev: string) => {
  switch (sev) {
    case 'Critical':
      return 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200'
    case 'High':
      return 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200'
    case 'Medium':
      return 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200'
    case 'Low':
      return 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
    default:
      return 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
  }
}

const formatTime = (dateString: string): string => {
  const date = new Date(dateString)
  const now = new Date()
  const diff = now.getTime() - date.getTime()
  const minutes = Math.floor(diff / 60000)
  
  if (minutes < 1) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`
  
  return date.toLocaleDateString()
}

onMounted(async () => {
  await signalRService.connect({
    onAlertReceived: (alert: HealthAlert) => {
      addAlert(alert)
      toastService.warning(`New ${alert.severity} alert from ${alert.deviceName}`)
    },
    onAlertUpdated: (alert: HealthAlert) => {
      updateAlert(alert.alertId, alert)
      toastService.info(`Alert updated: ${alert.message}`)
    },
    onAlertResolved: (alertId: string) => {
      // Refresh alerts to reflect resolution
      fetchAlerts()
      toastService.success('Alert resolved')
    }
  })

  // Subscribe to real-time alert updates
  await signalRService.subscribeToAlerts()
})
</script>
