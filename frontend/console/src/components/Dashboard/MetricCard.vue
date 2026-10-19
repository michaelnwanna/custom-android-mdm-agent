<template>
  <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
    <div class="flex items-start justify-between">
      <div>
        <p class="text-gray-600 dark:text-gray-400 text-sm font-medium">{{ title }}</p>
        <p :class="['text-3xl font-bold mt-2', colorClass]">{{ value }}</p>
      </div>
      <div :class="['w-12 h-12 rounded-lg flex items-center justify-center', bgColorClass]">
        <svg class="w-6 h-6 text-white" fill="currentColor" viewBox="0 0 24 24">
          <path d="M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2z" />
        </svg>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  title: string
  value: string | number
  icon?: string
  color?: 'primary' | 'success' | 'warning' | 'danger' | 'info'
}

const props = withDefaults(defineProps<Props>(), {
  color: 'primary'
})

const colorClass = computed(() => {
  switch (props.color) {
    case 'success':
      return 'text-success'
    case 'warning':
      return 'text-warning'
    case 'danger':
      return 'text-danger'
    case 'info':
      return 'text-info'
    default:
      return 'text-primary'
  }
})

const bgColorClass = computed(() => {
  switch (props.color) {
    case 'success':
      return 'bg-success'
    case 'warning':
      return 'bg-warning'
    case 'danger':
      return 'bg-danger'
    case 'info':
      return 'bg-info'
    default:
      return 'bg-primary'
  }
})
</script>
