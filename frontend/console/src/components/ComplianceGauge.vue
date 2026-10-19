<template>
  <div class="flex flex-col items-center gap-4">
    <!-- SVG Circular Gauge -->
    <div class="relative w-40 h-40">
      <svg class="w-full h-full transform -rotate-90" viewBox="0 0 100 100">
        <!-- Background circle -->
        <circle
          cx="50"
          cy="50"
          r="45"
          fill="none"
          :stroke="isDarkMode ? '#374151' : '#e5e7eb'"
          stroke-width="8"
        />
        
        <!-- Progress circle (animated) -->
        <circle
          cx="50"
          cy="50"
          r="45"
          fill="none"
          :stroke="gaugeColor"
          stroke-width="8"
          stroke-dasharray="282.7" <!-- 2 * π * 45 -->
          :stroke-dashoffset="strokeOffset"
          stroke-linecap="round"
          class="transition-all duration-1000"
        />
      </svg>
      
      <!-- Center content -->
      <div class="absolute inset-0 flex flex-col items-center justify-center">
        <div :class="['text-4xl font-bold', complianceTextClass]">
          {{ percentage }}%
        </div>
        <div class="text-sm font-medium text-gray-600 dark:text-gray-400">
          {{ label }}
        </div>
      </div>
    </div>

    <!-- Status badges -->
    <div class="flex gap-4 w-full">
      <div class="flex-1 bg-gray-50 dark:bg-gray-700/50 rounded-lg p-3 text-center">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Compliant</div>
        <div :class="['text-lg font-semibold', greenClass]">{{ compliantCount }}</div>
      </div>
      <div class="flex-1 bg-gray-50 dark:bg-gray-700/50 rounded-lg p-3 text-center">
        <div class="text-xs text-gray-600 dark:text-gray-400 font-medium">Non-compliant</div>
        <div :class="['text-lg font-semibold', redClass]">{{ nonCompliantCount }}</div>
      </div>
    </div>

    <!-- Progress text -->
    <div class="w-full text-center text-sm text-gray-600 dark:text-gray-400">
      <span class="font-medium">{{ compliantCount }}</span> of <span class="font-medium">{{ totalCount }}</span> devices compliant
    </div>

    <!-- Trending indicator -->
    <div v-if="trend" class="w-full flex items-center justify-center gap-2 p-3 bg-blue-50 dark:bg-blue-900/20 rounded-lg">
      <svg
        v-if="trend === 'up'"
        class="w-4 h-4 text-green-600 dark:text-green-400"
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
      >
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7h8m0 0v8m0-8L5.257 19.793M5 7v8m0-8H1m4 16v-4m12-4v4" />
      </svg>
      <svg
        v-else-if="trend === 'down'"
        class="w-4 h-4 text-red-600 dark:text-red-400"
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
      >
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 17H5m8 0H5m8 0V9m0 8l7.793-7.793M5 17v-4m12 4v-4" />
      </svg>
      <span class="text-sm font-medium text-blue-900 dark:text-blue-200">
        {{ trendText }}
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  percentage: number
  compliantCount: number
  nonCompliantCount: number
  label?: string
  trend?: 'up' | 'down' | null
  trendText?: string
  isDarkMode?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  label: 'Compliance',
  trend: null,
  trendText: '',
  isDarkMode: false
})

const totalCount = computed(() => props.compliantCount + props.nonCompliantCount)

const strokeOffset = computed(() => {
  // circumference = 2 * π * 45 ≈ 282.7
  const circumference = 282.7
  return circumference - (props.percentage / 100) * circumference
})

const gaugeColor = computed(() => {
  if (props.percentage >= 90) return '#10b981' // green-600
  if (props.percentage >= 75) return '#3b82f6' // blue-600
  if (props.percentage >= 50) return '#f59e0b' // amber-600
  return '#ef4444' // red-600
})

const complianceTextClass = computed(() => {
  if (props.percentage >= 90) return 'text-green-600 dark:text-green-400'
  if (props.percentage >= 75) return 'text-blue-600 dark:text-blue-400'
  if (props.percentage >= 50) return 'text-amber-600 dark:text-amber-400'
  return 'text-red-600 dark:text-red-400'
})

const greenClass = computed(() => {
  if (props.isDarkMode) return 'text-green-400'
  return 'text-green-600'
})

const redClass = computed(() => {
  if (props.isDarkMode) return 'text-red-400'
  return 'text-red-600'
})
</script>
