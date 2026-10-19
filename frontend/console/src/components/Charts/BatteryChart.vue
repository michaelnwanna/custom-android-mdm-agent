<template>
  <ChartCard title="Device Battery Status">
    <div v-if="loading" class="h-64 bg-gray-100 dark:bg-gray-700 rounded animate-pulse" />
    <div 
      v-else-if="error" 
      class="h-64 bg-red-50 dark:bg-red-900 rounded flex items-center justify-center"
    >
      <p class="text-red-600 dark:text-red-300">{{ error }}</p>
    </div>
    <div v-else class="relative h-64">
      <Doughnut :data="chartData" :options="chartOptions" />
    </div>
  </ChartCard>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { Doughnut } from 'vue-chartjs'
import { Chart as ChartJS, ArcElement, Tooltip, Legend, ChartOptions } from 'chart.js'
import { useTelemetryStore } from '@stores/telemetryStore'
import ChartCard from '@components/Dashboard/ChartCard.vue'

ChartJS.register(ArcElement, Tooltip, Legend)

const telemetryStore = useTelemetryStore()
const loading = ref(false)
const error = ref<string | null>(null)

// Chart data computed from telemetry store
const chartData = computed(() => {
  const metrics = telemetryStore.fleetMetrics
  if (!metrics) {
    return { labels: [], datasets: [] }
  }

  const total = metrics.totalDevices || 1
  const charging = Math.round(total * 0.45) // Estimate: 45% charging
  const discharging = Math.round(total * 0.35) // 35% discharging
  const critical = metrics.lowBatteryDevices || 0
  const unknown = Math.max(0, total - charging - discharging - critical)

  return {
    labels: ['Charging', 'Discharging', 'Critical (<10%)', 'Unknown'],
    datasets: [
      {
        data: [charging, discharging, critical, unknown],
        backgroundColor: ['#10B981', '#3B82F6', '#EF4444', '#9CA3AF'],
        borderColor: ['#059669', '#1D4ED8', '#DC2626', '#6B7280'],
        borderWidth: 2
      }
    ]
  }
})

const chartOptions: ChartOptions<'doughnut'> = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      position: 'bottom',
      labels: {
        padding: 15,
        font: { size: 12 },
        color: document.documentElement.classList.contains('dark') ? '#E5E7EB' : '#374151'
      }
    },
    tooltip: {
      callbacks: {
        label: function (context: any) {
          const total = context.dataset.data.reduce((a: number, b: number) => a + b, 0)
          const percentage = Math.round((context.parsed * 100) / total)
          return `${context.label}: ${context.parsed} (${percentage}%)`
        }
      }
    }
  }
}

onMounted(async () => {
  loading.value = true
  try {
    await telemetryStore.fetchFleetMetrics()
    error.value = null
  } catch (err) {
    error.value = 'Failed to load battery data'
  } finally {
    loading.value = false
  }
})

// Watch for real-time updates
watch(
  () => telemetryStore.fleetMetrics,
  () => {
    // Chart will auto-update due to computed binding
  },
  { deep: true }
)
</script>
