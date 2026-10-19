<template>
  <div class="space-y-2">
    <div v-if="loading" class="space-y-2">
      <div v-for="i in 3" :key="i" class="h-12 bg-gray-100 dark:bg-gray-700 rounded animate-pulse" />
    </div>
    <div v-else-if="error" class="text-red-600 dark:text-red-300 text-sm">{{ error }}</div>
    <div v-else-if="activeAlerts.length === 0" class="text-gray-500 text-center py-8">
      No active alerts
    </div>
    <div v-else>
      <div
        v-for="alert in activeAlerts.slice(0, 5)"
        :key="alert.alertId || alert.id"
        class="flex items-start justify-between p-3 bg-gray-50 dark:bg-gray-700 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-600 transition-colors group"
      >
        <div class="flex items-start space-x-3 flex-1 min-w-0">
          <div :class="['w-2 h-2 mt-2 rounded-full flex-shrink-0', getSeverityColor(alert.severity)]" />
          <div class="flex-1 min-w-0">
            <p class="text-sm font-medium text-gray-900 dark:text-white truncate">{{ alert.alertType || alert.type || 'Alert' }}</p>
            <p class="text-xs text-gray-600 dark:text-gray-400">{{ timeAgo(alert.createdAt || alert.timestamp || new Date().toISOString()) }}</p>
          </div>
        </div>
        <button
          @click="handleResolveAlert(alert.alertId || alert.id || '')"
          class="ml-2 px-2 py-1 text-xs bg-primary text-white rounded opacity-0 group-hover:opacity-100 hover:bg-blue-600 transition-all"
          :disabled="resolving.includes(alert.alertId || alert.id || '')"
        >
          {{ resolving.includes(alert.alertId || alert.id || '') ? '...' : 'Resolve' }}
        </button>
      </div>
      <router-link
        to="/alerts"
        class="block mt-4 text-center text-primary hover:underline text-sm font-medium"
      >
        View All Alerts →
      </router-link>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useAlertStore } from '@stores/alertStore'

const alertStore = useAlertStore()
const loading = ref(false)
const error = ref<string | null>(null)
const resolving = ref<string[]>([])

const activeAlerts = computed(() => alertStore.activeAlerts)

function getSeverityColor(severity: string): string {
  switch (severity) {
    case 'Critical':
      return 'bg-red-600'
    case 'High':
      return 'bg-orange-500'
    case 'Medium':
      return 'bg-amber-400'
    default:
      return 'bg-gray-400'
  }
}

function timeAgo(dateString: string): string {
  const now = new Date()
  const then = new Date(dateString)
  const diff = now.getTime() - then.getTime()
  const minutes = Math.floor(diff / 60000)

  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

async function handleResolveAlert(alertId: string) {
  resolving.value.push(alertId)
  try {
    await alertStore.resolveAlert(alertId)
    // Toast notification would go here
    console.log(`Alert ${alertId} resolved`)
  } catch (err) {
    error.value = 'Failed to resolve alert'
  } finally {
    resolving.value = resolving.value.filter(id => id !== alertId)
  }
}

onMounted(async () => {
  loading.value = true
  try {
    await alertStore.fetchAlerts()
    error.value = null
  } catch (err) {
    error.value = 'Failed to load alerts'
  } finally {
    loading.value = false
  }
})
</script>
