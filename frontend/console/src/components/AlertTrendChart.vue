<template>
  <div class="w-full h-full">
    <!-- Header with period selector -->
    <div class="flex items-center justify-between mb-6">
      <h3 class="text-lg font-semibold text-gray-900 dark:text-white">Alert Trend</h3>
      <div class="flex gap-2">
        <button
          v-for="p in ['24h', '7d', '30d']"
          :key="p"
          @click="selectedPeriod = p"
          :class="[
            'px-3 py-1 text-xs font-medium rounded-lg transition',
            selectedPeriod === p
              ? 'bg-blue-600 text-white'
              : 'bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-300 dark:hover:bg-gray-600'
          ]"
        >
          {{ p }}
        </button>
      </div>
    </div>

    <!-- Chart container -->
    <div class="relative w-full h-64">
      <!-- Grid lines -->
      <div class="absolute inset-0 flex flex-col-reverse justify-between pointer-events-none">
        <div
          v-for="(line, index) in 5"
          :key="index"
          class="border-t border-gray-200 dark:border-gray-700"
          :style="{ width: '100%' }"
        >
          <span class="text-xs text-gray-500 dark:text-gray-400 absolute -left-8">
            {{ index * 20 }}
          </span>
        </div>
      </div>

      <!-- SVG Chart -->
      <svg class="w-full h-full" viewBox="0 0 800 250" preserveAspectRatio="xMidYMid meet">
        <!-- Line chart background -->
        <defs>
          <linearGradient id="trendGradient" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" :stop-color="gradientStartColor" stop-opacity="0.3" />
            <stop offset="100%" :stop-color="gradientStartColor" stop-opacity="0.05" />
          </linearGradient>
        </defs>

        <!-- Area fill -->
        <path
          :d="areaPath"
          fill="url(#trendGradient)"
          class="transition-all duration-500"
        />

        <!-- Line -->
        <polyline
          :points="linePoints"
          fill="none"
          :stroke="lineColor"
          stroke-width="3"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="transition-all duration-500"
        />

        <!-- Data points -->
        <circle
          v-for="(point, index) in dataPoints"
          :key="index"
          :cx="point.x"
          :cy="point.y"
          r="4"
          :fill="lineColor"
          class="cursor-pointer hover:r-6 transition-all"
        >
          <title>{{ point.value }} alerts</title>
        </circle>

        <!-- X-axis labels -->
        <text
          v-for="(label, index) in xAxisLabels"
          :key="index"
          :x="(index * 800) / (xAxisLabels.length - 1)"
          y="240"
          text-anchor="middle"
          class="text-xs fill-gray-500 dark:fill-gray-400"
        >
          {{ label }}
        </text>
      </svg>
    </div>

    <!-- Legend -->
    <div class="mt-6 grid grid-cols-3 gap-4">
      <div class="bg-red-50 dark:bg-red-900/20 rounded-lg p-3">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Critical Alerts</div>
        <div class="text-xl font-bold text-red-600 dark:text-red-400">{{ criticalCount }}</div>
      </div>
      <div class="bg-yellow-50 dark:bg-yellow-900/20 rounded-lg p-3">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Warning Alerts</div>
        <div class="text-xl font-bold text-yellow-600 dark:text-yellow-400">{{ warningCount }}</div>
      </div>
      <div class="bg-blue-50 dark:bg-blue-900/20 rounded-lg p-3">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Info Alerts</div>
        <div class="text-xl font-bold text-blue-600 dark:text-blue-400">{{ infoCount }}</div>
      </div>
    </div>

    <!-- Statistics -->
    <div class="mt-6 grid grid-cols-2 gap-4">
      <div class="border border-gray-200 dark:border-gray-700 rounded-lg p-4">
        <div class="text-sm text-gray-600 dark:text-gray-400">Avg Alerts/Hour</div>
        <div class="text-2xl font-bold text-gray-900 dark:text-white mt-1">
          {{ avgAlertsPerHour.toFixed(1) }}
        </div>
      </div>
      <div class="border border-gray-200 dark:border-gray-700 rounded-lg p-4">
        <div class="text-sm text-gray-600 dark:text-gray-400">Peak Alerts</div>
        <div class="text-2xl font-bold text-gray-900 dark:text-white mt-1">
          {{ maxAlerts }}
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

interface Props {
  isDarkMode?: boolean
}

withDefaults(defineProps<Props>(), {
  isDarkMode: false
})

const selectedPeriod = ref('7d')

// Mock data for different periods
const mockData = {
  '24h': [12, 15, 18, 14, 22, 25, 20, 18, 16, 14, 12, 10, 8, 6, 5, 7, 9, 11, 13, 15, 16, 14, 12, 10],
  '7d': [45, 52, 48, 61, 55, 68, 72, 65, 58, 71, 64, 59, 62, 55, 48, 52, 58, 64, 61, 55, 48, 45, 50, 56],
  '30d': [200, 215, 190, 225, 210, 240, 235, 220, 215, 230, 225, 210, 205, 195, 210, 225, 240, 235, 220, 215, 205, 200, 215, 225]
}

const xAxisLabels = computed(() => {
  switch (selectedPeriod.value) {
    case '24h':
      return ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '24:00']
    case '7d':
      return ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
    case '30d':
      return ['1', '8', '15', '22', '30']
    default:
      return []
  }
})

const chartData = computed(() => mockData[selectedPeriod.value as keyof typeof mockData] || [])

const maxValue = computed(() => Math.max(...chartData.value))

const dataPoints = computed(() => {
  return chartData.value.map((value, index) => {
    const x = (index / (chartData.value.length - 1)) * 800
    const y = 220 - (value / maxValue.value) * 200
    return { x, y, value }
  })
})

const linePoints = computed(() => {
  return dataPoints.value.map(p => `${p.x},${p.y}`).join(' ')
})

const areaPath = computed(() => {
  if (dataPoints.value.length === 0) return ''
  const points = dataPoints.value.map(p => `${p.x},${p.y}`).join(' ')
  const lastPoint = dataPoints.value[dataPoints.value.length - 1]
  const firstPoint = dataPoints.value[0]
  return `M ${firstPoint.x} 220 L ${points} L ${lastPoint.x} 220 Z`
})

const lineColor = computed(() => {
  const avgValue = chartData.value.reduce((a, b) => a + b, 0) / chartData.value.length
  if (avgValue > maxValue.value * 0.7) return '#ef4444' // red
  if (avgValue > maxValue.value * 0.5) return '#f59e0b' // amber
  return '#3b82f6' // blue
})

const gradientStartColor = computed(() => lineColor.value)

const criticalCount = computed(() => Math.floor(chartData.value.reduce((a, b) => a + b, 0) * 0.35))
const warningCount = computed(() => Math.floor(chartData.value.reduce((a, b) => a + b, 0) * 0.45))
const infoCount = computed(() => Math.floor(chartData.value.reduce((a, b) => a + b, 0) * 0.20))

const avgAlertsPerHour = computed(() => {
  if (selectedPeriod.value === '24h') return chartData.value.reduce((a, b) => a + b, 0) / 24
  if (selectedPeriod.value === '7d') return chartData.value.reduce((a, b) => a + b, 0) / (7 * 24)
  return chartData.value.reduce((a, b) => a + b, 0) / (30 * 24)
})

const maxAlerts = computed(() => Math.max(...chartData.value))
</script>
