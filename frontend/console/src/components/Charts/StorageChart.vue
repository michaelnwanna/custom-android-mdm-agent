<template>
  <ChartCard title="Storage Usage Distribution">
    <div v-if="loading" class="h-64 bg-gray-100 dark:bg-gray-700 rounded animate-pulse" />
    <div 
      v-else-if="error" 
      class="h-64 bg-red-50 dark:bg-red-900 rounded flex items-center justify-center"
    >
      <p class="text-red-600 dark:text-red-300">{{ error }}</p>
    </div>
    <div v-else class="relative h-64">
      <Bar :data="chartData" :options="chartOptions" />
    </div>
  </ChartCard>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { Bar } from 'vue-chartjs'
import { 
  Chart as ChartJS, 
  BarElement, 
  CategoryScale, 
  LinearScale, 
  Tooltip, 
  Legend,
  ChartOptions 
} from 'chart.js'
import { useTelemetryStore } from '@stores/telemetryStore'
import ChartCard from '@components/Dashboard/ChartCard.vue'

ChartJS.register(BarElement, CategoryScale, LinearScale, Tooltip, Legend)

const telemetryStore = useTelemetryStore()
const loading = ref(false)
const error = ref<string | null>(null)

// Generate mock storage distribution data
const storageData = computed(() => {
  const metrics = telemetryStore.fleetMetrics
  if (!metrics) return []

  // Mock storage distribution (top 10 devices)
  return [
    { label: 'Device-001', usage: 87 },
    { label: 'Device-005', usage: 92 },
    { label: 'Device-012', usage: 65 },
    { label: 'Device-018', usage: 78 },
    { label: 'Device-023', usage: 88 },
    { label: 'Device-031', usage: 45 },
    { label: 'Device-042', usage: 72 },
    { label: 'Device-056', usage: 95 },
    { label: 'Device-067', usage: 58 },
    { label: 'Device-089', usage: 81 }
  ]
})

// Color based on usage percentage
const getStorageColor = (usage: number): string => {
  if (usage > 90) return '#EF4444' // Critical (red)
  if (usage > 80) return '#F59E0B' // Warning (orange)
  return '#10B981' // Healthy (green)
}

const chartData = computed(() => {
  const data = storageData.value
  return {
    labels: data.map(d => d.label),
    datasets: [
      {
        label: 'Storage Usage (%)',
        data: data.map(d => d.usage),
        backgroundColor: data.map(d => getStorageColor(d.usage)),
        borderColor: data.map(d => {
          const color = getStorageColor(d.usage)
          return color === '#EF4444' ? '#DC2626' : color === '#F59E0B' ? '#D97706' : '#059669'
        }),
        borderWidth: 1,
        borderRadius: 4
      }
    ]
  }
})

const chartOptions: ChartOptions<'bar'> = {
  indexAxis: 'y' as const,
  responsive: true,
  maintainAspectRatio: false,
  scales: {
    x: {
      beginAtZero: true,
      max: 100,
      ticks: {
        callback: (value) => `${value}%`,
        color: document.documentElement.classList.contains('dark') ? '#9CA3AF' : '#6B7280'
      },
      grid: {
        color: document.documentElement.classList.contains('dark') ? '#374151' : '#E5E7EB'
      }
    },
    y: {
      ticks: {
        color: document.documentElement.classList.contains('dark') ? '#9CA3AF' : '#6B7280',
        font: { size: 11 }
      },
      grid: {
        display: false
      }
    }
  },
  plugins: {
    legend: {
      display: true,
      labels: {
        padding: 15,
        font: { size: 12 },
        color: document.documentElement.classList.contains('dark') ? '#E5E7EB' : '#374151'
      }
    },
    tooltip: {
      callbacks: {
        label: function (context: any) {
          return `${context.parsed.x}% used`
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
    error.value = 'Failed to load storage data'
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
