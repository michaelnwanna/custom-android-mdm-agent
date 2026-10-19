<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Compliance Report</h1>
        <p class="text-gray-600 dark:text-gray-400 mt-1">
          Policy delivery status from device acknowledgements · refreshes every 30 seconds
        </p>
      </div>
      <div class="flex gap-3">
        <button
          type="button"
          :disabled="loading"
          @click="loadReport"
          class="px-4 py-2 border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-200 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50"
        >
          {{ loading ? 'Refreshing...' : 'Refresh' }}
        </button>
        <button
          @click="exportReport('csv')"
          class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition font-medium flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
          </svg>
          Export CSV
        </button>
        <button
          @click="exportReport('pdf')"
          class="px-4 py-2 bg-gray-600 text-white rounded-lg hover:bg-gray-700 transition font-medium flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
          </svg>
          Export PDF
        </button>
      </div>
    </div>

    <!-- Filters and Date Range -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
      <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
        <!-- Date Range Picker -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Start Date</label>
          <input
            v-model="startDate"
            type="date"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">End Date</label>
          <input
            v-model="endDate"
            type="date"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Policy Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Policy</label>
          <select
            v-model="selectedPolicy"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="">All Policies</option>
            <option v-for="policy in policies" :key="policy.policyId" :value="policy.policyId">
              {{ policy.name }}
            </option>
          </select>
        </div>

        <!-- Compliance Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Status</label>
          <select
            v-model="selectedStatus"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="">All Statuses</option>
            <option value="compliant">Compliant</option>
            <option value="non-compliant">Non-Compliant</option>
            <option value="pending">Pending</option>
            <option value="unassigned">Unassigned</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Summary Cards -->
    <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div class="flex items-center justify-between">
          <div>
            <p class="text-sm text-gray-600 dark:text-gray-400 font-medium">Total Devices</p>
            <p class="text-3xl font-bold text-gray-900 dark:text-white mt-2">{{ totalDevices }}</p>
          </div>
          <div class="p-3 bg-blue-100 dark:bg-blue-900/20 rounded-lg">
            <svg class="w-6 h-6 text-blue-600 dark:text-blue-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 18h.01M8 21h8a2 2 0 002-2V5a2 2 0 00-2-2H8a2 2 0 00-2 2v14a2 2 0 002 2z" />
            </svg>
          </div>
        </div>
      </div>

      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div class="flex items-center justify-between">
          <div>
            <p class="text-sm text-gray-600 dark:text-gray-400 font-medium">Compliant</p>
            <p class="text-3xl font-bold text-green-600 dark:text-green-400 mt-2">{{ compliantDevices }}</p>
          </div>
          <div class="p-3 bg-green-100 dark:bg-green-900/20 rounded-lg">
            <svg class="w-6 h-6 text-green-600 dark:text-green-400" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd" />
            </svg>
          </div>
        </div>
      </div>

      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div class="flex items-center justify-between">
          <div>
            <p class="text-sm text-gray-600 dark:text-gray-400 font-medium">Not Compliant</p>
            <p class="text-3xl font-bold text-red-600 dark:text-red-400 mt-2">{{ nonCompliantDevices }}</p>
          </div>
          <div class="p-3 bg-red-100 dark:bg-red-900/20 rounded-lg">
            <svg class="w-6 h-6 text-red-600 dark:text-red-400" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
            </svg>
          </div>
        </div>
      </div>

      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div class="flex items-center justify-between">
          <div>
            <p class="text-sm text-gray-600 dark:text-gray-400 font-medium">Compliance Rate</p>
            <p :class="['text-3xl font-bold mt-2', complianceRateClass]">{{ complianceRate }}%</p>
          </div>
          <div :class="['p-3 rounded-lg', complianceRateBgClass]">
            <svg class="w-6 h-6" :class="complianceRateClass" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
          </div>
        </div>
      </div>
    </div>

    <!-- Compliance Gauge Section -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
      <h2 class="text-xl font-bold text-gray-900 dark:text-white mb-6">Compliance Overview</h2>
      <div class="flex justify-center">
        <ComplianceGauge
          :percentage="complianceRate"
          :compliant-count="compliantDevices"
          :non-compliant-count="nonCompliantDevices"
          label="Acknowledged policy version"
          :is-dark-mode="isDarkMode"
        />
      </div>
    </div>

    <!-- Device Compliance Table -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
      <div class="p-6 border-b border-gray-200 dark:border-gray-700">
        <h2 class="text-xl font-bold text-gray-900 dark:text-white">Device Compliance Details</h2>
      </div>

      <div v-if="loading" class="p-8 text-center text-gray-600 dark:text-gray-400">
        Loading compliance report...
      </div>

      <div v-else-if="error" role="alert" class="m-6 rounded-lg border border-red-200 bg-red-50 p-4 text-red-800 dark:border-red-800 dark:bg-red-900/20 dark:text-red-200">
        {{ error }}
        <button type="button" class="ml-2 font-medium underline" @click="loadReport">Retry</button>
      </div>

      <div v-else-if="filteredDevices.length === 0" class="p-6 text-center">
        <svg class="w-12 h-12 text-gray-400 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4" />
        </svg>
        <p class="text-gray-600 dark:text-gray-400">No devices found matching the selected filters.</p>
      </div>

      <div v-else class="overflow-x-auto">
        <table class="w-full">
          <thead class="bg-gray-50 dark:bg-gray-700/50 border-b border-gray-200 dark:border-gray-700">
            <tr>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-600 dark:text-gray-300 uppercase tracking-wider">Device</th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-600 dark:text-gray-300 uppercase tracking-wider">Policy</th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-600 dark:text-gray-300 uppercase tracking-wider">Status</th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-600 dark:text-gray-300 uppercase tracking-wider">Compliance</th>
              <th class="px-6 py-3 text-left text-xs font-medium text-gray-600 dark:text-gray-300 uppercase tracking-wider">Last Checked</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-200 dark:divide-gray-700">
            <tr
              v-for="device in paginatedDevices"
              :key="device.deviceId"
              class="hover:bg-gray-50 dark:hover:bg-gray-700/50 transition"
            >
              <td class="px-6 py-4">
                <div class="font-medium text-gray-900 dark:text-white">{{ device.deviceName }}</div>
                <div class="text-sm text-gray-600 dark:text-gray-400">{{ device.deviceId }}</div>
              </td>
              <td class="px-6 py-4">
                <span class="text-sm text-gray-700 dark:text-gray-300">{{ device.policyName }}</span>
              </td>
              <td class="px-6 py-4">
                <span :class="['px-3 py-1 rounded-full text-sm font-medium', getStatusBadgeClass(device.status)]">
                  {{ device.status }}
                </span>
              </td>
              <td class="px-6 py-4">
                <div class="flex items-center gap-2">
                  <div class="w-16 bg-gray-200 dark:bg-gray-700 rounded-full h-2">
                    <div
                      class="h-2 rounded-full transition-all"
                      :class="getComplianceBarClass(device.compliance)"
                      :style="{ width: device.compliance + '%' }"
                    ></div>
                  </div>
                  <span class="text-sm font-medium text-gray-900 dark:text-white">{{ device.compliance }}%</span>
                </div>
              </td>
              <td class="px-6 py-4 text-sm text-gray-600 dark:text-gray-400">
                {{ formatDate(device.lastChecked) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="px-6 py-4 border-t border-gray-200 dark:border-gray-700 flex items-center justify-between">
        <div class="text-sm text-gray-600 dark:text-gray-400">
          Showing {{ startIndex + 1 }} to {{ Math.min(endIndex, filteredDevices.length) }} of {{ filteredDevices.length }}
        </div>
        <div class="flex gap-2">
          <button
            @click="currentPage--"
            :disabled="currentPage === 1"
            class="px-3 py-1 border border-gray-300 dark:border-gray-600 rounded-lg text-sm font-medium text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700/50 disabled:opacity-50 disabled:cursor-not-allowed transition"
          >
            Previous
          </button>
          <span class="px-3 py-1 text-sm text-gray-600 dark:text-gray-400">
            Page {{ currentPage }} of {{ totalPages }}
          </span>
          <button
            @click="currentPage++"
            :disabled="currentPage === totalPages"
            class="px-3 py-1 border border-gray-300 dark:border-gray-600 rounded-lg text-sm font-medium text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700/50 disabled:opacity-50 disabled:cursor-not-allowed transition"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import ComplianceGauge from '@components/ComplianceGauge.vue'
import toastService from '@services/toastService'
import type { Policy } from '@types'
import {
  fetchPolicies,
  fetchComplianceReport,
  type PolicyManagementDevice
} from '@/api/policyApi'
import {
  buildComplianceCsv,
  buildCompliancePrintHtml,
  formatComplianceDate
} from '@/utils/complianceReportExport'

interface ComplianceDevice {
  deviceId: string
  enrollmentId: string
  deviceName: string
  policyId?: string
  policyName: string
  status: PolicyManagementDevice['complianceStatus']
  compliance: number
  lastChecked: string
}

const policies = ref<Policy[]>([])
const devices = ref<ComplianceDevice[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const startDate = ref(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().split('T')[0])
const endDate = ref(new Date().toISOString().split('T')[0])
const selectedPolicy = ref('')
const selectedStatus = ref('')
const currentPage = ref(1)
const itemsPerPage = 10
const isDarkMode = ref(document.documentElement.classList.contains('dark'))

const filteredDevices = computed(() => {
  return devices.value.filter(device => {
    if (selectedPolicy.value && device.policyId !== selectedPolicy.value) return false
    if (selectedStatus.value && device.status !== selectedStatus.value) return false
    const checkedDate = new Date(device.lastChecked).toISOString().slice(0, 10)
    if (startDate.value && checkedDate < startDate.value) return false
    if (endDate.value && checkedDate > endDate.value) return false
    return true
  })
})

const totalDevices = computed(() => filteredDevices.value.length)
const compliantDevices = computed(() => filteredDevices.value.filter(d => d.status === 'compliant').length)
const nonCompliantDevices = computed(() => filteredDevices.value.length - compliantDevices.value)
const complianceRate = computed(() => {
  if (filteredDevices.value.length === 0) return 0
  return Math.round((compliantDevices.value / filteredDevices.value.length) * 100)
})

const totalPages = computed(() => Math.ceil(filteredDevices.value.length / itemsPerPage))
const startIndex = computed(() => (currentPage.value - 1) * itemsPerPage)
const endIndex = computed(() => startIndex.value + itemsPerPage)
const paginatedDevices = computed(() => filteredDevices.value.slice(startIndex.value, endIndex.value))

watch([startDate, endDate, selectedPolicy, selectedStatus], () => {
  currentPage.value = 1
})

const complianceRateClass = computed(() => {
  if (complianceRate.value >= 90) return 'text-green-600 dark:text-green-400'
  if (complianceRate.value >= 75) return 'text-blue-600 dark:text-blue-400'
  if (complianceRate.value >= 50) return 'text-amber-600 dark:text-amber-400'
  return 'text-red-600 dark:text-red-400'
})

const complianceRateBgClass = computed(() => {
  if (complianceRate.value >= 90) return 'bg-green-100 dark:bg-green-900/20'
  if (complianceRate.value >= 75) return 'bg-blue-100 dark:bg-blue-900/20'
  if (complianceRate.value >= 50) return 'bg-amber-100 dark:bg-amber-900/20'
  return 'bg-red-100 dark:bg-red-900/20'
})

const getStatusBadgeClass = (status: string) => {
  switch (status) {
    case 'compliant':
      return 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200'
    case 'non-compliant':
      return 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200'
    case 'pending':
      return 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200'
    case 'unassigned':
      return 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
    default:
      return 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
  }
}

const getComplianceBarClass = (compliance: number) => {
  if (compliance >= 90) return 'bg-green-600 dark:bg-green-500'
  if (compliance >= 75) return 'bg-blue-600 dark:bg-blue-500'
  if (compliance >= 50) return 'bg-amber-600 dark:bg-amber-500'
  return 'bg-red-600 dark:bg-red-500'
}

const exportReport = (format: 'csv' | 'pdf') => {
  const reportDate = new Date().toISOString().split('T')[0]
  if (format === 'csv') {
    const csv = buildComplianceCsv(filteredDevices.value, formatComplianceDate)
    const blob = new Blob([csv], { type: 'text/csv' })
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `compliance-report-${reportDate}.csv`
    a.click()
    window.setTimeout(() => window.URL.revokeObjectURL(url), 1000)
    toastService.success('Report exported as CSV')
  } else {
    const reportWindow = window.open('', '_blank')
    if (!reportWindow) {
      toastService.error('Unable to open the print window. Allow pop-ups and try again.')
      return
    }

    const html = buildCompliancePrintHtml(filteredDevices.value, {
      complianceRate: complianceRate.value,
      compliantDevices: compliantDevices.value,
      nonCompliantDevices: nonCompliantDevices.value,
      totalDevices: totalDevices.value,
      startDate: startDate.value,
      endDate: endDate.value
    }, formatComplianceDate)
    reportWindow.document.open()
    reportWindow.document.write(html)
    reportWindow.document.close()
    reportWindow.setTimeout(() => {
      reportWindow.focus()
      reportWindow.print()
    }, 250)
    toastService.info('Choose “Save as PDF” in the print dialog to download the report.')
  }
}

const formatDate = formatComplianceDate

const loadReport = async () => {
  loading.value = true
  error.value = null
  try {
    const [report, policyResult] = await Promise.all([
      fetchComplianceReport(),
      fetchPolicies({ page: 1, pageSize: 500 })
    ])
    devices.value = report.map(device => ({
      deviceId: device.deviceId,
      enrollmentId: device.enrollmentId,
      deviceName: device.deviceName,
      policyId: device.policyId,
      policyName: device.policyName || 'Unassigned',
      status: device.complianceStatus,
      compliance: device.compliancePercentage,
      lastChecked: device.lastChecked
    }))
    policies.value = policyResult.items
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to load compliance report'
  } finally {
    loading.value = false
  }
}

let reportRefreshTimer: number | undefined

onMounted(() => {
  void loadReport()
  reportRefreshTimer = window.setInterval(() => {
    if (!loading.value) void loadReport()
  }, 30_000)
})

onBeforeUnmount(() => {
  if (reportRefreshTimer !== undefined) window.clearInterval(reportRefreshTimer)
})
</script>
