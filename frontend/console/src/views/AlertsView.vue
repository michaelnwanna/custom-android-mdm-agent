<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Alerts</h1>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1">
          {{ alertStore.activeAlerts.length }} active · {{ alertStore.criticalAlerts.length }} critical
        </p>
      </div>
      <button
        @click="refreshAlerts"
        class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition flex items-center gap-2"
      >
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
        </svg>
        Refresh
      </button>
    </div>

    <!-- Filters & Search -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
      <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
        <!-- Search -->
        <div class="md:col-span-2">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Search</label>
          <input
            v-model="searchQuery"
            type="text"
            placeholder="Search by device name or message..."
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Status Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Status</label>
          <select
            v-model="selectedStatus"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="all">All Alerts</option>
            <option value="active">Active</option>
            <option value="resolved">Resolved</option>
          </select>
        </div>

        <!-- Severity Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Severity</label>
          <select
            v-model="selectedSeverity"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="all">All Levels</option>
            <option value="Critical">Critical</option>
            <option value="High">High</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>
        </div>
      </div>

      <!-- Bulk Actions -->
      <div v-if="selectedAlerts.length > 0" class="flex items-center justify-between border-t border-gray-200 dark:border-gray-700 pt-4">
        <span class="text-sm text-gray-600 dark:text-gray-400">{{ selectedAlerts.length }} selected</span>
        <button
          @click="resolveSelectedAlerts"
          :disabled="resolvingBulk"
          class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
          </svg>
          Resolve Selected ({{ selectedAlerts.length }})
        </button>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="alertStore.loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading alerts...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="alertStore.error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 flex items-start gap-3">
      <svg class="w-5 h-5 text-red-600 dark:text-red-400 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
        <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
      </svg>
      <div>
        <h3 class="font-medium text-red-900 dark:text-red-200">Error loading alerts</h3>
        <p class="text-sm text-red-700 dark:text-red-300 mt-1">{{ alertStore.error }}</p>
      </div>
    </div>

    <!-- Empty State -->
    <div v-else-if="filteredAlerts.length === 0" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">No alerts found</h3>
      <p class="text-gray-600 dark:text-gray-400">
        {{ searchQuery || selectedStatus !== 'all' || selectedSeverity !== 'all' 
          ? 'Try adjusting your filters' 
          : 'Great! No active alerts right now' }}
      </p>
    </div>

    <!-- Alerts Table -->
    <div v-else class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
      <table class="w-full">
        <thead class="bg-gray-50 dark:bg-gray-700 border-b border-gray-200 dark:border-gray-600">
          <tr>
            <th class="px-6 py-3 text-left">
              <input
                type="checkbox"
                :checked="selectedAlerts.length > 0 && selectedAlerts.length === filteredAlerts.length"
                @change="toggleSelectAll"
                class="rounded"
              />
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
          <tr
            v-for="alert in paginatedAlerts"
            :key="alert.alertId"
            class="hover:bg-gray-50 dark:hover:bg-gray-700/50 transition"
          >
            <td class="px-6 py-4">
              <input
                type="checkbox"
                :checked="selectedAlerts.includes(alert.alertId || '')"
                @change="toggleAlert(alert.alertId || '')"
                class="rounded"
              />
            </td>
            <td class="px-6 py-4">
              <span :class="[
                'px-3 py-1 rounded-full text-xs font-medium',
                alert.severity === 'Critical' ? 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200' :
                alert.severity === 'High' ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200' :
                alert.severity === 'Medium' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
                'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
              ]">
                {{ alert.severity }}
              </span>
            </td>
            <td class="px-6 py-4">
              <div class="flex flex-col">
                <span class="font-medium text-gray-900 dark:text-white">{{ alert.deviceName || 'Unknown Device' }}</span>
                <span class="text-sm text-gray-600 dark:text-gray-400 truncate">{{ alert.message }}</span>
              </div>
            </td>
            <td class="px-6 py-4 text-sm text-gray-600 dark:text-gray-400">
              {{ alert.alertType || alert.type || 'General' }}
            </td>
            <td class="px-6 py-4 text-sm text-gray-600 dark:text-gray-400">
              {{ formatTime(alert.createdAt || alert.timestamp) }}
            </td>
            <td class="px-6 py-4">
              <span :class="[
                'px-2 py-1 rounded text-xs font-medium',
                !alert.resolvedAt && !alert.resolved
                  ? 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
                  : 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
              ]">
                {{ !alert.resolvedAt && !alert.resolved ? 'Active' : 'Resolved' }}
              </span>
            </td>
            <td class="px-6 py-4 text-right">
              <div class="flex justify-end gap-2">
                <router-link
                  :to="`/alerts/${alert.alertId}`"
                  class="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300 text-sm font-medium"
                >
                  View
                </router-link>
                <button
                  v-if="!alert.resolvedAt && !alert.resolved"
                  @click="resolveAlert(alert.alertId || '')"
                  :disabled="resolvingId === alert.alertId"
                  class="text-green-600 dark:text-green-400 hover:text-green-800 dark:hover:text-green-300 text-sm font-medium disabled:opacity-50"
                >
                  {{ resolvingId === alert.alertId ? 'Resolving...' : 'Resolve' }}
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>

      <!-- Pagination -->
      <div class="bg-white dark:bg-gray-800 border-t border-gray-200 dark:border-gray-700 px-6 py-4 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <label class="text-sm text-gray-600 dark:text-gray-400">Items per page:</label>
          <select
            v-model.number="pageSize"
            class="px-2 py-1 border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-gray-700 text-gray-900 dark:text-white text-sm"
          >
            <option :value="10">10</option>
            <option :value="20">20</option>
            <option :value="50">50</option>
            <option :value="100">100</option>
          </select>
        </div>

        <div class="text-sm text-gray-600 dark:text-gray-400">
          Showing {{ startIndex + 1 }}-{{ Math.min(endIndex, filteredAlerts.length) }} of {{ filteredAlerts.length }}
        </div>

        <div class="flex items-center gap-1">
          <button
            @click="currentPage = Math.max(1, currentPage - 1)"
            :disabled="currentPage === 1"
            class="px-3 py-1 border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 text-sm"
          >
            Previous
          </button>

          <div class="flex gap-1 mx-2">
            <template v-for="page in paginationRange" :key="page">
              <button
                v-if="page === '...'"
                class="px-2 py-1 text-gray-600 dark:text-gray-400"
              >
                ...
              </button>
              <button
                v-else
                @click="currentPage = page as number"
                :class="[
                  'px-3 py-1 rounded text-sm font-medium',
                  currentPage === page
                    ? 'bg-blue-600 text-white'
                    : 'border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700'
                ]"
              >
                {{ page }}
              </button>
            </template>
          </div>

          <button
            @click="currentPage = Math.min(totalPages, currentPage + 1)"
            :disabled="currentPage === totalPages"
            class="px-3 py-1 border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 text-sm"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useAlertStore } from '@stores/alertStore'
import type { HealthAlert } from '@types'

const alertStore = useAlertStore()
const searchQuery = ref('')
const selectedStatus = ref('active')
const selectedSeverity = ref('all')
const selectedAlerts = ref<string[]>([])
const currentPage = ref(1)
const pageSize = ref(20)
const resolvingId = ref<string | null>(null)
const resolvingBulk = ref(false)

// Computed: Filtered alerts based on search and filters
const filteredAlerts = computed(() => {
  let result = [...alertStore.alerts]

  // Status filter
  if (selectedStatus.value === 'active') {
    result = result.filter(a => !a.resolvedAt && !a.resolved)
  } else if (selectedStatus.value === 'resolved') {
    result = result.filter(a => !!a.resolvedAt || a.resolved)
  }

  // Severity filter
  if (selectedSeverity.value !== 'all') {
    result = result.filter(a => a.severity === selectedSeverity.value)
  }

  // Search filter
  if (searchQuery.value) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(a =>
      (a.deviceName || '').toLowerCase().includes(query) ||
      a.message.toLowerCase().includes(query)
    )
  }

  return result
})

// Reset to page 1 when filters change
const _ = computed(() => {
  searchQuery.value
  selectedStatus.value
  selectedSeverity.value
  currentPage.value = 1
  selectedAlerts.value = []
  return null
})

// Computed: Pagination info
const totalPages = computed(() => Math.ceil(filteredAlerts.value.length / pageSize.value))
const startIndex = computed(() => (currentPage.value - 1) * pageSize.value)
const endIndex = computed(() => startIndex.value + pageSize.value)

const paginatedAlerts = computed(() =>
  filteredAlerts.value.slice(startIndex.value, endIndex.value)
)

// Computed: Pagination range (smart, with ellipsis)
const paginationRange = computed(() => {
  const pages: (number | string)[] = []
  const delta = 2
  const range = {
    start: Math.round(currentPage.value - delta),
    end: Math.round(currentPage.value + delta)
  }

  range.start = Math.max(range.start, 1)
  range.end = Math.min(range.end, totalPages.value)

  const includePageNumbers = (from: number, to: number) => {
    for (let i = from; i <= to; i++) {
      pages.push(i)
    }
  }

  if (range.start > 1) {
    includePageNumbers(1, 1)
    if (range.start > 2) pages.push('...')
  }

  includePageNumbers(range.start, range.end)

  if (range.end < totalPages.value) {
    if (range.end < totalPages.value - 1) pages.push('...')
    includePageNumbers(totalPages.value, totalPages.value)
  }

  return pages
})

// Methods
function toggleAlert(alertId: string) {
  const index = selectedAlerts.value.indexOf(alertId)
  if (index > -1) {
    selectedAlerts.value.splice(index, 1)
  } else {
    selectedAlerts.value.push(alertId)
  }
}

function toggleSelectAll() {
  if (selectedAlerts.value.length === paginatedAlerts.value.length) {
    selectedAlerts.value = []
  } else {
    selectedAlerts.value = paginatedAlerts.value.map(a => a.alertId || '')
  }
}

async function resolveAlert(alertId: string) {
  resolvingId.value = alertId
  try {
    await alertStore.resolveAlert(alertId)
    selectedAlerts.value = selectedAlerts.value.filter(id => id !== alertId)
  } finally {
    resolvingId.value = null
  }
}

async function resolveSelectedAlerts() {
  resolvingBulk.value = true
  try {
    await Promise.all(selectedAlerts.value.map(id => alertStore.resolveAlert(id)))
    selectedAlerts.value = []
  } finally {
    resolvingBulk.value = false
  }
}

function refreshAlerts() {
  alertStore.fetchAlerts()
}

function formatTime(timestamp?: string): string {
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
  return date.toLocaleDateString()
}

onMounted(() => {
  alertStore.fetchAlerts()
})
</script>
