<template>
  <div class="w-full h-full">
    <!-- Header -->
    <div class="flex items-center justify-between mb-6">
      <div>
        <h3 class="text-lg font-semibold text-gray-900 dark:text-white">Compliance Trend</h3>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1">Last 30 days</p>
      </div>
      <div class="flex gap-2">
        <button
          v-for="metric in ['Compliant', 'Non-Compliant']"
          :key="metric"
          @click="toggleMetric(metric)"
          :class="[
            'px-3 py-1 text-xs font-medium rounded-lg transition',
            visibleMetrics.includes(metric)
              ? 'bg-blue-600 text-white'
              : 'bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-300 dark:hover:bg-gray-600'
          ]"
        >
          {{ metric }}
        </button>
      </div>
    </div>

    <!-- Chart container -->
    <div class="relative w-full h-80">
      <!-- Grid background -->
      <div class="absolute inset-0 flex flex-col-reverse justify-between pointer-events-none">
        <div
          v-for="(line, index) in 5"
          :key="index"
          class="border-t border-gray-200 dark:border-gray-700"
          :style="{ width: '100%' }"
        >
          <span class="text-xs text-gray-500 dark:text-gray-400 absolute -left-12">
            {{ index * 20 }}%
          </span>
        </div>
      </div>

      <!-- SVG Chart -->
      <svg class="w-full h-full" viewBox="0 0 800 300" preserveAspectRatio="xMidYMid meet">
        <defs>
          <!-- Gradients for compliant -->
          <linearGradient id="compliantGradient" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" stop-color="#10b981" stop-opacity="0.2" />
            <stop offset="100%" stop-color="#10b981" stop-opacity="0.05" />
          </linearGradient>
          
          <!-- Gradients for non-compliant -->
          <linearGradient id="nonCompliantGradient" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" stop-color="#ef4444" stop-opacity="0.2" />
            <stop offset="100%" stop-color="#ef4444" stop-opacity="0.05" />
          </linearGradient>
        </defs>

        <!-- Compliant area -->
        <path
          v-if="visibleMetrics.includes('Compliant')"
          :d="compliantAreaPath"
          fill="url(#compliantGradient)"
          class="transition-all duration-500"
        />

        <!-- Non-compliant area -->
        <path
          v-if="visibleMetrics.includes('Non-Compliant')"
          :d="nonCompliantAreaPath"
          fill="url(#nonCompliantGradient)"
          class="transition-all duration-500"
        />

        <!-- Compliant line -->
        <polyline
          v-if="visibleMetrics.includes('Compliant')"
          :points="compliantLinePoints"
          fill="none"
          stroke="#10b981"
          stroke-width="3"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="transition-all duration-500"
        />

        <!-- Non-compliant line -->
        <polyline
          v-if="visibleMetrics.includes('Non-Compliant')"
          :points="nonCompliantLinePoints"
          fill="none"
          stroke="#ef4444"
          stroke-width="3"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="transition-all duration-500"
        />

        <!-- Data points - Compliant -->
        <circle
          v-for="(point, index) in compliantDataPoints"
          v-if="visibleMetrics.includes('Compliant')"
          :key="`c-${index}`"
          :cx="point.x"
          :cy="point.y"
          r="3"
          fill="#10b981"
          class="cursor-pointer hover:r-5 transition-all"
        >
          <title>Day {{ index + 1 }}: {{ point.value }}% compliant</title>
        </circle>

        <!-- Data points - Non-compliant -->
        <circle
          v-for="(point, index) in nonCompliantDataPoints"
          v-if="visibleMetrics.includes('Non-Compliant')"
          :key="`nc-${index}`"
          :cx="point.x"
          :cy="point.y"
          r="3"
          fill="#ef4444"
          class="cursor-pointer hover:r-5 transition-all"
        >
          <title>Day {{ index + 1 }}: {{ point.value }}% non-compliant</title>
        </circle>

        <!-- X-axis labels (every 5 days) -->
        <text
          v-for="(day, index) in [1, 6, 11, 16, 21, 26, 30]"
          :key="day"
          :x="((day - 1) / 29) * 800"
          y="290"
          text-anchor="middle"
          class="text-xs fill-gray-500 dark:fill-gray-400"
        >
          Day {{ day }}
        </text>
      </svg>
    </div>

    <!-- Legend -->
    <div class="mt-6 grid grid-cols-2 gap-4">
      <div class="flex items-center gap-3 p-3 bg-gray-50 dark:bg-gray-700/50 rounded-lg">
        <div class="w-3 h-3 bg-green-600 rounded-full"></div>
        <div>
          <div class="text-sm font-medium text-gray-900 dark:text-white">Compliant Devices</div>
          <div class="text-xs text-gray-600 dark:text-gray-400">Trend: {{ compliantTrend }}</div>
        </div>
      </div>
      <div class="flex items-center gap-3 p-3 bg-gray-50 dark:bg-gray-700/50 rounded-lg">
        <div class="w-3 h-3 bg-red-600 rounded-full"></div>
        <div>
          <div class="text-sm font-medium text-gray-900 dark:text-white">Non-Compliant Devices</div>
          <div class="text-xs text-gray-600 dark:text-gray-400">Trend: {{ nonCompliantTrend }}</div>
        </div>
      </div>
    </div>

    <!-- Statistics -->
    <div class="mt-6 grid grid-cols-4 gap-3">
      <div class="bg-green-50 dark:bg-green-900/20 rounded-lg p-3 border border-green-200 dark:border-green-800">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Current Compliant</div>
        <div class="text-lg font-bold text-green-600 dark:text-green-400 mt-1">{{ currentCompliance }}%</div>
      </div>
      <div class="bg-red-50 dark:bg-red-900/20 rounded-lg p-3 border border-red-200 dark:border-red-800">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Current Non-Compliant</div>
        <div class="text-lg font-bold text-red-600 dark:text-red-400 mt-1">{{ 100 - currentCompliance }}%</div>
      </div>
      <div class="bg-blue-50 dark:bg-blue-900/20 rounded-lg p-3 border border-blue-200 dark:border-blue-800">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Avg 30d Compliant</div>
        <div class="text-lg font-bold text-blue-600 dark:text-blue-400 mt-1">{{ avgCompliance }}%</div>
      </div>
      <div class="bg-purple-50 dark:bg-purple-900/20 rounded-lg p-3 border border-purple-200 dark:border-purple-800">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Improvement</div>
        <div :class="['text-lg font-bold mt-1', improvementClass]">{{ improvementValue }}</div>
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

const visibleMetrics = ref<string[]>(['Compliant', 'Non-Compliant'])

// Mock data: 30 days of compliance percentages
const mockCompliantData = [
  82, 84, 83, 85, 86, 87, 86, 85, 87, 88,
  89, 88, 90, 91, 90, 92, 93, 92, 91, 93,
  94, 95, 94, 96, 95, 97, 96, 97, 98, 96
]

const currentCompliance = computed(() => mockCompliantData[mockCompliantData.length - 1])

const avgCompliance = computed(() => {
  const sum = mockCompliantData.reduce((a, b) => a + b, 0)
  return Math.round(sum / mockCompliantData.length)
})

const compliantTrend = computed(() => {
  const current = currentCompliance.value
  const previous = mockCompliantData[mockCompliantData.length - 8]
  if (current > previous) return '↑ Improving'
  if (current < previous) return '↓ Declining'
  return '→ Stable'
})

const nonCompliantTrend = computed(() => {
  const nonCompliant = 100 - currentCompliance.value
  const previousNonCompliant = 100 - mockCompliantData[mockCompliantData.length - 8]
  if (nonCompliant < previousNonCompliant) return '↓ Improving'
  if (nonCompliant > previousNonCompliant) return '↑ Declining'
  return '→ Stable'
})

const improvementValue = computed(() => {
  const start = mockCompliantData[0]
  const end = mockCompliantData[mockCompliantData.length - 1]
  const change = end - start
  return change > 0 ? `+${change}%` : `${change}%`
})

const improvementClass = computed(() => {
  const change = currentCompliance.value - mockCompliantData[0]
  if (change > 0) return 'text-green-600 dark:text-green-400'
  if (change < 0) return 'text-red-600 dark:text-red-400'
  return 'text-gray-600 dark:text-gray-400'
})

const compliantDataPoints = computed(() => {
  const maxValue = 100
  return mockCompliantData.map((value, index) => {
    const x = (index / (mockCompliantData.length - 1)) * 800
    const y = 240 - (value / maxValue) * 200
    return { x, y, value }
  })
})

const nonCompliantDataPoints = computed(() => {
  const maxValue = 100
  return mockCompliantData.map((value, index) => {
    const nonCompliant = 100 - value
    const x = (index / (mockCompliantData.length - 1)) * 800
    const y = 240 - (nonCompliant / maxValue) * 200
    return { x, y, value: nonCompliant }
  })
})

const compliantLinePoints = computed(() => {
  return compliantDataPoints.value.map(p => `${p.x},${p.y}`).join(' ')
})

const nonCompliantLinePoints = computed(() => {
  return nonCompliantDataPoints.value.map(p => `${p.x},${p.y}`).join(' ')
})

const compliantAreaPath = computed(() => {
  if (compliantDataPoints.value.length === 0) return ''
  const points = compliantDataPoints.value.map(p => `${p.x},${p.y}`).join(' ')
  const lastPoint = compliantDataPoints.value[compliantDataPoints.value.length - 1]
  const firstPoint = compliantDataPoints.value[0]
  return `M ${firstPoint.x} 240 L ${points} L ${lastPoint.x} 240 Z`
})

const nonCompliantAreaPath = computed(() => {
  if (nonCompliantDataPoints.value.length === 0) return ''
  const points = nonCompliantDataPoints.value.map(p => `${p.x},${p.y}`).join(' ')
  const lastPoint = nonCompliantDataPoints.value[nonCompliantDataPoints.value.length - 1]
  const firstPoint = nonCompliantDataPoints.value[0]
  return `M ${firstPoint.x} 240 L ${points} L ${lastPoint.x} 240 Z`
})

const toggleMetric = (metric: string) => {
  const index = visibleMetrics.value.indexOf(metric)
  if (index > -1) {
    visibleMetrics.value.splice(index, 1)
  } else {
    visibleMetrics.value.push(metric)
  }
}
</script>
