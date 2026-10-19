<template>
  <div class="space-y-6">
    <!-- Page header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Devices</h1>
        <p class="text-gray-600 dark:text-gray-400 mt-2">Manage and monitor your fleet devices</p>
      </div>
      <button
        @click="refreshDevices"
        class="px-4 py-2 bg-primary text-white rounded-lg hover:bg-blue-600 transition-colors flex items-center gap-2"
      >
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
        </svg>
        Refresh
      </button>
    </div>

    <!-- Filters and Search -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-4">
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
        <!-- Search -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Search</label>
          <input
            :value="searchQuery"
            @input="handleSearch($event.target.value)"
            type="text"
            placeholder="Device name, model, serial..."
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg dark:bg-gray-700 dark:text-white focus:outline-none focus:ring-2 focus:ring-primary"
          />
        </div>

        <!-- Status Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Status</label>
          <select
            :value="statusFilter"
            @change="handleStatusFilterChange($event.target.value || null)"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg dark:bg-gray-700 dark:text-white focus:outline-none focus:ring-2 focus:ring-primary"
          >
            <option :value="null">All Statuses</option>
            <option value="Enrolled">Enrolled</option>
            <option value="Suspended">Suspended</option>
            <option value="Decommissioned">Decommissioned</option>
          </select>
        </div>

        <!-- Clear Filters -->
        <div v-if="hasFilters" class="flex items-end">
          <button
            @click="clearFiltersHandler"
            class="w-full px-3 py-2 text-sm bg-gray-300 dark:bg-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-400 dark:hover:bg-gray-500 transition-colors"
          >
            Clear Filters
          </button>
        </div>
      </div>

      <!-- Bulk Actions -->
      <div v-if="selectedDevices.length > 0" class="mt-4 pt-4 border-t border-gray-200 dark:border-gray-700 flex items-center gap-4">
        <span class="text-sm text-gray-600 dark:text-gray-400">
          {{ selectedDevices.length }} device(s) selected
        </span>
        <button
          @click="suspendSelected"
          class="px-3 py-1 text-sm bg-yellow-500 text-white rounded hover:bg-yellow-600 transition-colors"
        >
          Suspend
        </button>
        <button
          @click="decommissionSelected"
          class="px-3 py-1 text-sm bg-red-500 text-white rounded hover:bg-red-600 transition-colors"
        >
          Decommission
        </button>
        <button
          @click="clearSelection"
          class="px-3 py-1 text-sm bg-gray-400 text-white rounded hover:bg-gray-500 transition-colors"
        >
          Clear
        </button>
      </div>
    </div>

    <!-- Devices Table -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
      <!-- Loading State -->
      <div v-if="loading" class="p-8 text-center">
        <div class="inline-block">
          <div class="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin" />
        </div>
        <p class="text-gray-600 dark:text-gray-400 mt-4">Loading devices...</p>
      </div>

      <!-- Error State -->
      <div v-else-if="error" class="p-8 text-center text-red-600 dark:text-red-400">
        <p class="font-medium">{{ error }}</p>
        <button
          @click="refreshDevices"
          class="mt-4 px-4 py-2 bg-primary text-white rounded hover:bg-blue-600 transition-colors"
        >
          Try Again
        </button>
      </div>

      <!-- Empty State -->
      <div v-else-if="filteredList.length === 0" class="p-8 text-center">
        <svg class="w-12 h-12 mx-auto text-gray-400 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 12c4.418 0 8-1.79 8-4s-3.582-4-8-4-8 1.79-8 4 3.582 4 8 4zm0 0c-4.418 0-8 1.79-8 4v4c0 2.21 3.582 4 8 4s8-1.79 8-4v-4c0-2.21-3.582-4-8-4z" />
        </svg>
        <p class="text-gray-600 dark:text-gray-400">No devices found</p>
      </div>

      <!-- Table -->
      <table v-else class="w-full">
        <thead class="bg-gray-50 dark:bg-gray-700 border-b border-gray-200 dark:border-gray-600">
          <tr>
            <th class="px-4 py-3 text-left">
              <input
                type="checkbox"
                @change="toggleSelectAll"
                :checked="selectedDevices.length === filteredList.length && filteredList.length > 0"
                class="rounded"
              />
            </th>
            <th class="px-4 py-3 text-left text-sm font-semibold text-gray-700 dark:text-gray-300">Device Name</th>
            <th class="px-4 py-3 text-left text-sm font-semibold text-gray-700 dark:text-gray-300">Model</th>
            <th class="px-4 py-3 text-left text-sm font-semibold text-gray-700 dark:text-gray-300">OS Version</th>
            <th class="px-4 py-3 text-left text-sm font-semibold text-gray-700 dark:text-gray-300">Status</th>
            <th class="px-4 py-3 text-left text-sm font-semibold text-gray-700 dark:text-gray-300">Last Sync</th>
            <th class="px-4 py-3 text-center text-sm font-semibold text-gray-700 dark:text-gray-300">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-200 dark:divide-gray-700">
          <tr
            v-for="device in devices"
            :key="device.deviceId"
            class="hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors"
          >
            <td class="px-4 py-3">
              <input
                type="checkbox"
                :checked="(device as any).selected || false"
                @change="toggleDevice(device.deviceId)"
                class="rounded"
              />
            </td>
            <td class="px-4 py-3">
              <div class="font-medium text-gray-900 dark:text-white">{{ device.deviceName }}</div>
              <div class="text-xs text-gray-500 dark:text-gray-400">{{ device.serialNumber }}</div>
            </td>
            <td class="px-4 py-3 text-gray-700 dark:text-gray-300">{{ device.model }}</td>
            <td class="px-4 py-3 text-gray-700 dark:text-gray-300">{{ device.osVersion }}</td>
            <td class="px-4 py-3">
              <span
                :class="[
                  'inline-block px-3 py-1 rounded-full text-xs font-semibold',
                  statusBadgeClass(device.status)
                ]"
              >
                {{ device.status }}
              </span>
            </td>
            <td class="px-4 py-3 text-gray-700 dark:text-gray-300 text-sm">
              {{ formatDate(device.lastSyncDate) }}
            </td>
            <td class="px-4 py-3 text-center">
              <button
                @click="handleDeviceClick(device.deviceId)"
                class="inline-block px-3 py-1 text-sm bg-primary text-white rounded hover:bg-blue-600 transition-colors"
              >
                View
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Pagination -->
    <div v-if="totalPages > 1" class="flex items-center justify-between">
      <div class="text-sm text-gray-600 dark:text-gray-400">
        Showing {{ (currentPage - 1) * itemsPerPage + 1 }} to
        {{ Math.min(currentPage * itemsPerPage, totalCount) }} of {{ totalCount }} devices
      </div>
      <div class="flex gap-2">
        <button
          @click="previousPage"
          :disabled="currentPage === 1"
          class="px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded hover:bg-gray-300 dark:hover:bg-gray-600 transition-colors disabled:opacity-50"
        >
          Previous
        </button>
        <div class="flex items-center gap-1">
          <button
            v-for="page in paginationRange"
            :key="page"
            @click="goToPage(page)"
            :class="[
              'px-3 py-2 rounded transition-colors',
              currentPage === page
                ? 'bg-primary text-white'
                : 'bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-300 dark:hover:bg-gray-600'
            ]"
          >
            {{ page }}
          </button>
        </div>
        <button
          @click="nextPage"
          :disabled="currentPage === totalPages"
          class="px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded hover:bg-gray-300 dark:hover:bg-gray-600 transition-colors disabled:opacity-50"
        >
          Next
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useDevices } from '@/composables/useDevices'
import toastService from '@services/toastService'

const router = useRouter()

// Use the composable
const {
  devices,
  loading,
  error,
  currentPage,
  totalPages,
  hasNextPage,
  searchQuery,
  statusFilter,
  fetchDevices,
  setSearch,
  setStatusFilter,
  goToPage,
  refresh
} = useDevices({ pageSize: 20, autoLoad: true })

// Selected devices for bulk actions
const selectedDevices = computed<string[]>(() => {
  return devices.value
    .filter(d => (d as any).selected)
    .map(d => d.deviceId)
})

// Computed helpers
const hasFilters = computed(() => searchQuery.value !== '' || statusFilter.value !== null)

const paginationRange = computed(() => {
  const delta = 2
  const range: number[] = []
  const rangeWithDots: (number | string)[] = []
  let l: number | null = null

  for (let i = 1; i <= totalPages.value; i++) {
    if (i === 1 || i === totalPages.value || (i >= currentPage.value - delta && i <= currentPage.value + delta)) {
      range.push(i)
    }
  }

  range.forEach((i) => {
    if (l !== null) {
      if (i - l === 2) {
        rangeWithDots.push(l + 1)
      } else if (i - l !== 1) {
        rangeWithDots.push('...')
      }
    }
    rangeWithDots.push(i)
    l = i
  })

  return rangeWithDots
})

// Methods
const handleDeviceClick = (deviceId: string) => {
  router.push({ name: 'DeviceDetail', params: { id: deviceId } })
}

const handleSearch = (query: string) => {
  setSearch(query)
  fetchDevices(1)
}

const handleStatusFilterChange = (status: string | null) => {
  setStatusFilter(status)
  fetchDevices(1)
}

const handlePageChange = (page: number | string) => {
  if (typeof page === 'number') {
    goToPage(page)
  }
}

const clearFiltersHandler = () => {
  setSearch('')
  setStatusFilter(null)
  fetchDevices(1)
}

const toggleDevice = (deviceId: string) => {
  // Toggle selected state in the device object
  const device = devices.value.find(d => d.deviceId === deviceId)
  if (device) {
    (device as any).selected = !(device as any).selected
  }
}

const toggleSelectAll = () => {
  const allSelected = devices.value.every(d => (d as any).selected)
  devices.value.forEach(d => {
    (d as any).selected = !allSelected
  })
}

const clearSelection = () => {
  devices.value.forEach(d => {
    (d as any).selected = false
  })
}

const suspendSelected = async () => {
  if (selectedDevices.value.length === 0) return
  if (!confirm(`Suspend ${selectedDevices.value.length} device(s)?`)) return
  
  try {
    // TODO: Call batch API to suspend devices
    toastService.success(`${selectedDevices.value.length} device(s) suspended`)
    clearSelection()
    await refresh()
  } catch (err) {
    toastService.error('Failed to suspend devices')
  }
}

const decommissionSelected = async () => {
  if (selectedDevices.value.length === 0) return
  if (!confirm(`Decommission ${selectedDevices.value.length} device(s)? This cannot be undone.`)) return
  
  try {
    // TODO: Call batch API to decommission devices
    toastService.success(`${selectedDevices.value.length} device(s) decommissioned`)
    clearSelection()
    await refresh()
  } catch (err) {
    toastService.error('Failed to decommission devices')
  }
}

const suspendSelected = async () => {
  if (!confirm(`Suspend ${selectedDevices.value.length} device(s)?`)) return
  
  try {
    // TODO: Call API to suspend devices
    toastService.success(`${selectedDevices.value.length} device(s) suspended`)
    clearSelection()
    await refreshDevices()
  } catch (err) {
    toastService.error('Failed to suspend devices')
  }
}

const decommissionSelected = async () => {
  if (!confirm(`Decommission ${selectedDevices.value.length} device(s)? This cannot be undone.`)) return
  
  try {
    // TODO: Call API to decommission devices
    toastService.success(`${selectedDevices.value.length} device(s) decommissioned`)
    clearSelection()
    await refreshDevices()
  } catch (err) {
    toastService.error('Failed to decommission devices')
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

// Note: useDevices composable handles autoLoading via onMounted
// The composable already has autoLoad: true by default
</script>

<style scoped>
input[type='checkbox'] {
  @apply cursor-pointer;
}
</style>
