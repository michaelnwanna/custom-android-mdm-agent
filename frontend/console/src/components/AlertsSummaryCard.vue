<template>
  <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg overflow-hidden hover:shadow-xl transition-shadow">
    <!-- Header -->
    <div class="bg-gradient-to-r from-red-500 to-red-600 dark:from-red-700 dark:to-red-800 px-6 py-4">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-3">
          <div class="bg-white dark:bg-red-900 p-2 rounded-lg">
            <svg class="w-6 h-6 text-red-600 dark:text-red-200" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
            </svg>
          </div>
          <h3 class="text-white font-semibold text-lg">Alerts</h3>
        </div>
        <router-link
          to="/alerts"
          class="text-white hover:bg-white hover:bg-opacity-20 px-3 py-1 rounded text-sm font-medium transition"
        >
          View All →
        </router-link>
      </div>
    </div>

    <!-- Content -->
    <div class="p-6 space-y-6">
      <!-- Metrics Grid -->
      <div class="grid grid-cols-3 gap-4">
        <!-- Total Alerts -->
        <div class="bg-gradient-to-br from-red-50 to-red-100 dark:from-red-900/30 dark:to-red-800/30 p-4 rounded-lg">
          <p class="text-xs text-red-600 dark:text-red-400 font-medium mb-1">Total</p>
          <p class="text-2xl font-bold text-red-900 dark:text-red-100">{{ totalAlerts }}</p>
        </div>

        <!-- Active Alerts -->
        <div class="bg-gradient-to-br from-orange-50 to-orange-100 dark:from-orange-900/30 dark:to-orange-800/30 p-4 rounded-lg">
          <p class="text-xs text-orange-600 dark:text-orange-400 font-medium mb-1">Active</p>
          <p class="text-2xl font-bold text-orange-900 dark:text-orange-100">{{ activeAlerts }}</p>
        </div>

        <!-- Critical Alerts -->
        <div class="bg-gradient-to-br from-red-50 to-red-100 dark:from-red-900/40 dark:to-red-800/40 p-4 rounded-lg border-2 border-red-300 dark:border-red-700">
          <p class="text-xs text-red-600 dark:text-red-400 font-medium mb-1">🔴 Critical</p>
          <p class="text-2xl font-bold text-red-900 dark:text-red-100">{{ criticalAlerts }}</p>
        </div>
      </div>

      <!-- Alert Severity Breakdown -->
      <div class="space-y-2">
        <p class="text-sm font-semibold text-gray-700 dark:text-gray-300">By Severity</p>
        <div class="space-y-1">
          <!-- Critical -->
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">Critical</span>
            <div class="flex items-center gap-2 flex-1 ml-2">
              <div class="flex-1 bg-gray-200 dark:bg-gray-700 rounded-full h-2">
                <div class="bg-red-600 h-2 rounded-full transition-all" :style="{ width: getCriticalPercent + '%' }"></div>
              </div>
              <span class="text-xs text-red-600 dark:text-red-400 font-semibold w-8 text-right">{{ criticalAlerts }}</span>
            </div>
          </div>

          <!-- High -->
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">High</span>
            <div class="flex items-center gap-2 flex-1 ml-2">
              <div class="flex-1 bg-gray-200 dark:bg-gray-700 rounded-full h-2">
                <div class="bg-orange-500 h-2 rounded-full transition-all" :style="{ width: getHighPercent + '%' }"></div>
              </div>
              <span class="text-xs text-orange-600 dark:text-orange-400 font-semibold w-8 text-right">{{ highAlerts }}</span>
            </div>
          </div>

          <!-- Medium -->
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">Medium</span>
            <div class="flex items-center gap-2 flex-1 ml-2">
              <div class="flex-1 bg-gray-200 dark:bg-gray-700 rounded-full h-2">
                <div class="bg-yellow-500 h-2 rounded-full transition-all" :style="{ width: getMediumPercent + '%' }"></div>
              </div>
              <span class="text-xs text-yellow-600 dark:text-yellow-400 font-semibold w-8 text-right">{{ mediumAlerts }}</span>
            </div>
          </div>

          <!-- Low -->
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">Low</span>
            <div class="flex items-center gap-2 flex-1 ml-2">
              <div class="flex-1 bg-gray-200 dark:bg-gray-700 rounded-full h-2">
                <div class="bg-blue-500 h-2 rounded-full transition-all" :style="{ width: getLowPercent + '%' }"></div>
              </div>
              <span class="text-xs text-blue-600 dark:text-blue-400 font-semibold w-8 text-right">{{ lowAlerts }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Trend Info -->
      <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
        <p class="text-xs text-gray-600 dark:text-gray-400 mb-2">Status</p>
        <div class="flex items-center gap-2">
          <span v-if="alertTrend > 0" class="inline-flex items-center gap-1 text-red-600 dark:text-red-400 text-sm">
            <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
              <path d="M7 16a1 1 0 01-.707-1.707l6-6a1 1 0 011.414 0l6 6a1 1 0 01-1.414 1.414L13 11.414V16a1 1 0 01-2 0v-4.586L7.707 14.293A1 1 0 017 16z" />
            </svg>
            {{ Math.abs(alertTrend) }}% increase (24h)
          </span>
          <span v-else-if="alertTrend < 0" class="inline-flex items-center gap-1 text-green-600 dark:text-green-400 text-sm">
            <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
              <path d="M13 4a1 1 0 01.707 1.707l-6 6a1 1 0 01-1.414 0l-6-6a1 1 0 011.414-1.414L7 8.586V4a1 1 0 012 0v4.586l4.293-4.293A1 1 0 0113 4z" />
            </svg>
            {{ Math.abs(alertTrend) }}% decrease (24h)
          </span>
          <span v-else class="inline-flex items-center gap-1 text-gray-600 dark:text-gray-400 text-sm">
            <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM7 9a1 1 0 100-2 1 1 0 000 2zm6 0a1 1 0 100-2 1 1 0 000 2z" clip-rule="evenodd" />
            </svg>
            Stable (24h)
          </span>
        </div>
      </div>

      <!-- Recent Alerts -->
      <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
        <p class="text-xs font-semibold text-gray-700 dark:text-gray-300 mb-3">Recent Alerts</p>
        <div v-if="recentAlerts.length > 0" class="space-y-2">
          <div
            v-for="alert in recentAlerts.slice(0, 3)"
            :key="alert.alertId"
            class="flex items-start gap-2 p-2 bg-gray-50 dark:bg-gray-700/50 rounded-lg"
          >
            <div
              :class="['flex-shrink-0 w-2 h-2 rounded-full mt-1.5', getSeverityColor(alert.severity)]"
            />
            <div class="flex-1 min-w-0">
              <p class="text-xs font-medium text-gray-900 dark:text-white truncate">{{ alert.message }}</p>
              <p class="text-xs text-gray-500 dark:text-gray-400">{{ alert.deviceName }}</p>
            </div>
          </div>
        </div>
        <div v-else class="text-center py-2 text-xs text-gray-500 dark:text-gray-400">
          No recent alerts
        </div>
      </div>
    </div>

    <!-- Footer -->
    <div class="bg-gray-50 dark:bg-gray-700/50 px-6 py-3 border-t border-gray-200 dark:border-gray-700">
      <p class="text-xs text-gray-600 dark:text-gray-400">
        Last updated: {{ formatTime(lastUpdated) }}
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useAlerts } from '@/composables/useAlerts'
import signalRService from '@services/signalRService'
import type { HealthAlert } from '@types'

const { alerts, fetchAlerts } = useAlerts({ autoLoad: false })

const lastUpdated = ref(new Date().toISOString())
const alertTrend = ref(0)
const previousAlertCount = ref(0)

const totalAlerts = computed(() => alerts.value.length)

const activeAlerts = computed(() =>
  alerts.value.filter(a => !a.resolved && !a.resolvedAt).length
)

const criticalAlerts = computed(() =>
  alerts.value.filter(a => !a.resolved && !a.resolvedAt && a.severity === 'Critical').length
)

const highAlerts = computed(() =>
  alerts.value.filter(a => !a.resolved && !a.resolvedAt && a.severity === 'High').length
)

const mediumAlerts = computed(() =>
  alerts.value.filter(a => !a.resolved && !a.resolvedAt && a.severity === 'Medium').length
)

const lowAlerts = computed(() =>
  alerts.value.filter(a => !a.resolved && !a.resolvedAt && a.severity === 'Low').length
)

const recentAlerts = computed(() =>
  alerts.value.filter(a => !a.resolved && !a.resolvedAt).slice(0, 5)
)

const getCriticalPercent = computed(() => {
  if (activeAlerts.value === 0) return 0
  return (criticalAlerts.value / activeAlerts.value) * 100
})

const getHighPercent = computed(() => {
  if (activeAlerts.value === 0) return 0
  return (highAlerts.value / activeAlerts.value) * 100
})

const getMediumPercent = computed(() => {
  if (activeAlerts.value === 0) return 0
  return (mediumAlerts.value / activeAlerts.value) * 100
})

const getLowPercent = computed(() => {
  if (activeAlerts.value === 0) return 0
  return (lowAlerts.value / activeAlerts.value) * 100
})

const getSeverityColor = (severity: string) => {
  switch (severity) {
    case 'Critical':
      return 'bg-red-600'
    case 'High':
      return 'bg-orange-500'
    case 'Medium':
      return 'bg-yellow-500'
    case 'Low':
      return 'bg-blue-500'
    default:
      return 'bg-gray-400'
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
  return date.toLocaleDateString()
}

const calculateTrend = () => {
  const diff = activeAlerts.value - previousAlertCount.value
  if (previousAlertCount.value === 0) {
    alertTrend.value = 0
  } else {
    alertTrend.value = Math.round((diff / previousAlertCount.value) * 100)
  }
  previousAlertCount.value = activeAlerts.value
}

onMounted(async () => {
  await fetchAlerts()
  previousAlertCount.value = activeAlerts.value

  // Set up auto-refresh
  const refreshInterval = setInterval(async () => {
    await fetchAlerts()
    calculateTrend()
    lastUpdated.value = new Date().toISOString()
  }, 30000) // Refresh every 30 seconds

  // SignalR real-time updates
  await signalRService.connect({
    onAlertReceived: () => {
      fetchAlerts()
      lastUpdated.value = new Date().toISOString()
    },
    onAlertResolved: () => {
      fetchAlerts()
      calculateTrend()
      lastUpdated.value = new Date().toISOString()
    }
  })

  onUnmounted(() => {
    clearInterval(refreshInterval)
  })
})
</script>
