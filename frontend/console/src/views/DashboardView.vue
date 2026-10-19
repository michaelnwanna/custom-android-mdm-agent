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
      <BatteryChart />

      <!-- Storage Usage Chart -->
      <StorageChart />
    </div>

    <!-- Bottom Section -->
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- At-Risk Devices -->
      <ChartCard title="At-Risk Devices" class="lg:col-span-2">
        <AtRiskDevices />
      </ChartCard>

      <!-- Recent Alerts -->
      <ChartCard title="Recent Alerts">
        <RecentAlerts />
      </ChartCard>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useTelemetryStore } from '@stores/telemetryStore'
import { useAlertStore } from '@stores/alertStore'
import MetricCard from '@components/Dashboard/MetricCard.vue'
import ChartCard from '@components/Dashboard/ChartCard.vue'
import BatteryChart from '@components/Charts/BatteryChart.vue'
import StorageChart from '@components/Charts/StorageChart.vue'
import AtRiskDevices from '@components/Dashboard/AtRiskDevices.vue'
import RecentAlerts from '@components/Dashboard/RecentAlerts.vue'

const telemetryStore = useTelemetryStore()
const alertStore = useAlertStore()

const metrics = computed(() => telemetryStore.fleetMetrics)
const healthScore = computed(() => telemetryStore.healthScore)

onMounted(async () => {
  await telemetryStore.fetchFleetMetrics()
  await alertStore.fetchAlerts()
})
</script>
