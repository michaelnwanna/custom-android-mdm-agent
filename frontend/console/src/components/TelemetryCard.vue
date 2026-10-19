<template>
  <div :class="['p-4 bg-gradient-to-br rounded-lg shadow-sm transition-all', colorClass]">
    <div class="flex items-start justify-between">
      <div class="flex-1">
        <p class="text-sm font-medium text-gray-600 dark:text-gray-400 mb-2">{{ label }}</p>
        <div class="flex items-baseline gap-2">
          <span class="text-3xl font-bold text-gray-900 dark:text-white">{{ formattedValue }}</span>
          <span v-if="unit" class="text-lg text-gray-600 dark:text-gray-400">{{ unit }}</span>
        </div>
        <p v-if="subtext" class="text-xs text-gray-500 dark:text-gray-500 mt-2">{{ subtext }}</p>
      </div>
      <div :class="['w-12 h-12 rounded-full flex items-center justify-center', iconBgClass]">
        <component :is="icon" class="w-6 h-6 text-white" />
      </div>
    </div>

    <!-- Progress bar for percentage metrics -->
    <div v-if="isPercentage" class="mt-4">
      <div class="w-full bg-gray-200 dark:bg-gray-600 rounded-full h-2">
        <div
          :class="['h-2 rounded-full transition-all duration-300', progressBarClass]"
          :style="{ width: `${value}%` }"
        />
      </div>
      <div class="text-xs text-gray-500 dark:text-gray-400 mt-1 flex justify-between">
        <span>0%</span>
        <span>100%</span>
      </div>
    </div>

    <!-- Last updated time -->
    <div class="text-xs text-gray-500 dark:text-gray-400 mt-3">
      Updated: {{ lastUpdated }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  label: string
  value: number
  unit?: string
  isPercentage?: boolean
  type?: 'battery' | 'storage' | 'memory' | 'cpu' | 'signal'
  lastUpdatedAt?: string
  subtext?: string
}

const props = withDefaults(defineProps<Props>(), {
  isPercentage: false,
  type: 'battery'
})

const colorClass = computed(() => {
  switch (props.type) {
    case 'battery':
      return 'from-green-50 to-green-100 dark:from-green-900/30 dark:to-green-800/30'
    case 'storage':
      return 'from-blue-50 to-blue-100 dark:from-blue-900/30 dark:to-blue-800/30'
    case 'memory':
      return 'from-purple-50 to-purple-100 dark:from-purple-900/30 dark:to-purple-800/30'
    case 'cpu':
      return 'from-orange-50 to-orange-100 dark:from-orange-900/30 dark:to-orange-800/30'
    case 'signal':
      return 'from-indigo-50 to-indigo-100 dark:from-indigo-900/30 dark:to-indigo-800/30'
    default:
      return 'from-gray-50 to-gray-100 dark:from-gray-900/30 dark:to-gray-800/30'
  }
})

const iconBgClass = computed(() => {
  switch (props.type) {
    case 'battery':
      return 'bg-green-500'
    case 'storage':
      return 'bg-blue-500'
    case 'memory':
      return 'bg-purple-500'
    case 'cpu':
      return 'bg-orange-500'
    case 'signal':
      return 'bg-indigo-500'
    default:
      return 'bg-gray-500'
  }
})

const progressBarClass = computed(() => {
  if (props.value >= 80) return 'bg-red-500'
  if (props.value >= 60) return 'bg-yellow-500'
  return 'bg-green-500'
})

const icon = computed(() => {
  switch (props.type) {
    case 'battery':
      return BatteryIcon
    case 'storage':
      return StorageIcon
    case 'memory':
      return MemoryIcon
    case 'cpu':
      return CpuIcon
    case 'signal':
      return SignalIcon
    default:
      return InfoIcon
  }
})

const formattedValue = computed(() => {
  if (props.isPercentage) {
    return `${Math.round(props.value)}%`
  }
  
  // Format storage in GB or MB
  if (props.unit === 'GB' || props.unit === 'MB') {
    return props.value.toFixed(1)
  }
  
  return props.value.toFixed(0)
})

const lastUpdated = computed(() => {
  if (!props.lastUpdatedAt) return 'Just now'
  
  const date = new Date(props.lastUpdatedAt)
  const now = new Date()
  const diff = now.getTime() - date.getTime()
  const seconds = Math.floor(diff / 1000)
  const minutes = Math.floor(seconds / 60)
  
  if (seconds < 60) return `${seconds}s ago`
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  
  return date.toLocaleDateString()
})

const BatteryIcon = { 
  template: `<svg fill="currentColor" viewBox="0 0 24 24"><path d="M17 4h-1V2h-4v2H7c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 12H7V6h10v10z"/></svg>`
}

const StorageIcon = {
  template: `<svg fill="currentColor" viewBox="0 0 24 24"><path d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V5h14v14zm-5.04-6.71l-2.75 3.54h2.79v2h-4v-1.35L12.45 9h-2.79V7h4v1.29z"/></svg>`
}

const MemoryIcon = {
  template: `<svg fill="currentColor" viewBox="0 0 24 24"><path d="M15 9H9v6h6V9zm-2 4h-2v-2h2v2zm8-8h-6V1h-2v4H9V1H7v4H1v2h6v6H1v2h6v6H1v2h18V3zm-2 16H3V5h16v14z"/></svg>`
}

const CpuIcon = {
  template: `<svg fill="currentColor" viewBox="0 0 24 24"><path d="M9 2C5.14 2 2 5.13 2 9c0 3.86 3.14 7 7 7s7-3.14 7-7c0-3.87-3.14-7-7-7zm3.5 7c0 1.93-1.57 3.5-3.5 3.5S5 10.93 5 9s1.57-3.5 3.5-3.5S12.5 7.07 12.5 9zm9-8h-2v3h-3v2h3v3h2v-3h3V6h-3V3z"/></svg>`
}

const SignalIcon = {
  template: `<svg fill="currentColor" viewBox="0 0 24 24"><path d="M1 9l2 2c4.97-4.97 13.03-4.97 18 0l2-2C16.93 2.93 7.08 2.93 1 9zm8 8l3 3 3-3c-1.65-1.66-4.34-1.66-6 0zm-4-4l2 2c2.76-2.76 7.24-2.76 10 0l2-2C15.14 9.14 8.87 9.14 5 13z"/></svg>`
}

const InfoIcon = {
  template: `<svg fill="currentColor" viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>`
}
</script>
