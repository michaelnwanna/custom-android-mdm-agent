<template>
  <header class="bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 sticky top-0 z-40">
    <div class="px-8 py-4 flex items-center justify-between">
      <!-- Left side -->
      <div class="flex items-center space-x-4">
        <h2 class="text-2xl font-bold text-gray-900 dark:text-white">{{ pageTitle }}</h2>
      </div>

      <!-- Right side -->
      <div class="flex items-center space-x-4">
        <!-- Real-time status indicator -->
        <div class="flex items-center space-x-2 px-3 py-2 bg-gray-100 dark:bg-gray-700 rounded-lg">
          <div 
            :class="[
              'w-3 h-3 rounded-full transition-colors',
              connectionStatusColor
            ]" 
          />
          <span class="text-sm font-medium text-gray-700 dark:text-gray-300">
            {{ connectionStatusLabel }}
          </span>
        </div>

        <!-- Notifications -->
        <button class="relative p-2 text-gray-600 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg transition-colors" title="Notifications">
          <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
          </svg>
          <span v-if="unreadNotifications > 0" class="absolute top-1 right-1 w-2 h-2 bg-red-500 rounded-full" />
        </button>

        <!-- User menu -->
        <div class="flex items-center space-x-3 pl-4 border-l border-gray-200 dark:border-gray-700">
          <div class="text-right">
            <p class="text-sm font-medium text-gray-900 dark:text-white">Admin User</p>
            <p class="text-xs text-gray-600 dark:text-gray-400">Administrator</p>
          </div>
          <div class="w-10 h-10 bg-primary text-white rounded-full flex items-center justify-center font-bold">
            A
          </div>
        </div>
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import signalRService from '@services/signalRService'
import type { ConnectionState } from '@services/signalRService'

const route = useRoute()
const connectionState = ref<ConnectionState>('disconnected')
const unreadNotifications = ref(0)

const pageTitle = computed(() => {
  const routeName = route.name as string
  return routeName || 'Dashboard'
})

const connectionStatusLabel = computed(() => {
  switch (connectionState.value) {
    case 'connected':
      return 'Connected'
    case 'connecting':
      return 'Connecting...'
    case 'disconnected':
      return 'Disconnected'
    case 'failed':
      return 'Connection Failed'
    default:
      return 'Unknown'
  }
})

const connectionStatusColor = computed(() => {
  switch (connectionState.value) {
    case 'connected':
      return 'bg-green-500 animate-pulse'
    case 'connecting':
      return 'bg-yellow-500 animate-pulse'
    case 'disconnected':
      return 'bg-gray-400'
    case 'failed':
      return 'bg-red-500'
    default:
      return 'bg-gray-400'
  }
})

onMounted(() => {
  // Subscribe to connection state changes
  signalRService.connect({
    onConnectionStateChanged: (state) => {
      connectionState.value = state
    },
  })
})
</script>

<style scoped>
@keyframes pulse {
  0%, 100% {
    opacity: 1;
  }
  50% {
    opacity: 0.5;
  }
}

.animate-pulse {
  animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}
</style>
