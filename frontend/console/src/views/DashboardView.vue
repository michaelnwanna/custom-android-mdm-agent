<template>
  <div class="space-y-6">
    <!-- Page header -->
    <div class="mb-8">
      <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Dashboard</h1>
      <p class="text-gray-600 dark:text-gray-400 mt-2">Real-time fleet health and device status overview</p>
    </div>

    <!-- Key Metrics -->
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
      <MetricCard
        title="Total Devices"
        :value="metrics?.totalDevices || 0"
        icon="devices"
        color="primary"
      />
      <MetricCard
        title="Online Devices"
        :value="metrics?.enrolledDevices || 0"
        icon="signal"
        color="success"
      />
      <MetricCard
        title="Active Alerts"
        :value="metrics?.activeAlerts || 0"
        icon="alert"
        color="warning"
      />
      <MetricCard
        title="Fleet Health"
        :value="`${healthScore}%`"
        icon="health"
        color="info"
      />
    </div>

    <!-- Charts Row -->
    <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
      <!-- Battery Distribution Chart -->
      <ChartCard title="Device Battery Status">
        <div class="h-64 bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center">
          <span class="text-gray-500">Battery chart placeholder</span>
        </div>
      </ChartCard>

      <!-- Storage Usage Chart -->
      <ChartCard title="Storage Usage Distribution">
        <div class="h-64 bg-gray-100 dark:bg-gray-700 rounded flex items-center justify-center">
          <span class="text-gray-500">Storage chart placeholder</span>
        </div>
      </ChartCard>
    </div>

    <!-- Bottom Section -->
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- At-Risk Devices -->
      <ChartCard title="At-Risk Devices" class="lg:col-span-2">
        <div class="space-y-2">
          <div v-if="!atRiskDevices || atRiskDevices.length === 0" class="text-gray-500 text-center py-8">
            All devices are healthy
          </div>
          <div 
            v-for="device in atRiskDevices?.slice(0, 5)"
            :key="device.deviceId"
            class="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-700 rounded"
          >
            <div>
              <p class="font-medium text-gray-900 dark:text-white">{{ device.deviceName }}</p>
              <p class="text-sm text-gray-600 dark:text-gray-400">{{ device.riskReason }}</p>
            </div>
            <span class="px-3 py-1 bg-danger text-white rounded-full text-sm">At Risk</span>
          </div>
        </div>
      </ChartCard>

      <!-- Recent Alerts -->
      <ChartCard title="Recent Alerts">
        <div class="space-y-2">
          <div v-if="!recentAlerts || recentAlerts.length === 0" class="text-gray-500 text-center py-8">
            No recent alerts
          </div>
          <div 
            v-for="alert in recentAlerts?.slice(0, 5)"
            :key="alert.alertId"
            class="flex items-start space-x-3 p-2"
          >
            <div :class="['w-2 h-2 mt-1.5 rounded-full flex-shrink-0', getSeverityColor(alert.severity)]" />
            <div class="flex-1 min-w-0">
              <p class="text-sm font-medium text-gray-900 dark:text-white truncate">{{ alert.alertType }}</p>
              <p class="text-xs text-gray-600 dark:text-gray-400">{{ timeAgo(alert.createdAt) }}</p>
            </div>
          </div>
        </div>
      </ChartCard>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useTelemetryStore } from '@stores/telemetryStore'
import { useAlertStore } from '@stores/alertStore'
import MetricCard from '@components/Dashboard/MetricCard.vue'
import ChartCard from '@components/Dashboard/ChartCard.vue'

const telemetryStore = useTelemetryStore()
const alertStore = useAlertStore()

const metrics = computed(() => telemetryStore.fleetMetrics)
const healthScore = computed(() => telemetryStore.healthScore)
const recentAlerts = computed(() => alertStore.activeAlerts)
const atRiskDevices = ref<any[]>([])

onMounted(async () => {
  await telemetryStore.fetchFleetMetrics()
  await alertStore.fetchAlerts()
})

function getSeverityColor(severity: string): string {
  switch (severity) {
    case 'Critical':
      return 'bg-danger'
    case 'High':
      return 'bg-warning'
    case 'Medium':
      return 'bg-info'
    default:
      return 'bg-gray-400'
  }
}

function timeAgo(date: string): string {
  const now = new Date()
  const then = new Date(date)
  const diff = now.getTime() - then.getTime()
  const minutes = Math.floor(diff / 60000)
  
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}
</script>
