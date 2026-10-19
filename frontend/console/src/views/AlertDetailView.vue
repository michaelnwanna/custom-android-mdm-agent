<template>
  <div class="space-y-6">
    <!-- Header with Back Button -->
    <div class="flex items-center gap-4">
      <button
        @click="router.back()"
        class="p-2 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-lg transition"
      >
        <svg class="w-6 h-6 text-gray-600 dark:text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
        </svg>
      </button>
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Alert Details</h1>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1" v-if="alert">{{ alert.type || 'Alert' }}</p>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading alert details...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
      <div class="flex items-start gap-3">
        <svg class="w-5 h-5 text-red-600 dark:text-red-400 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
        </svg>
        <div>
          <h3 class="font-medium text-red-900 dark:text-red-200">Error loading alert</h3>
          <p class="text-sm text-red-700 dark:text-red-300 mt-1">{{ error }}</p>
        </div>
      </div>
    </div>

    <!-- Alert Not Found -->
    <div v-else-if="!alert" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">Alert not found</h3>
      <p class="text-gray-600 dark:text-gray-400">This alert may have been deleted or is no longer available.</p>
    </div>

    <!-- Alert Details -->
    <div v-else class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- Main Content -->
      <div class="lg:col-span-2 space-y-6">
        <!-- Alert Card -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
          <!-- Alert Header -->
          <div :class="['px-6 py-4 border-b border-gray-200 dark:border-gray-700', getSeverityBgClass(alert.severity)]">
            <div class="flex items-start justify-between">
              <div>
                <h2 class="text-2xl font-bold mb-2">{{ alert.message }}</h2>
                <p class="text-sm opacity-75">{{ alert.type || 'Alert' }} · {{ formatTime(alert.timestamp || alert.createdAt) }}</p>
              </div>
              <span :class="['px-4 py-2 rounded-full text-sm font-semibold', getSeverityBadgeClass(alert.severity)]">
                {{ alert.severity }}
              </span>
            </div>
          </div>

          <!-- Alert Details -->
          <div class="p-6 space-y-4">
            <div class="grid grid-cols-2 gap-4">
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Alert ID</p>
                <p class="font-mono text-sm text-gray-900 dark:text-white break-all">{{ alert.alertId }}</p>
              </div>
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Device</p>
                <router-link :to="`/devices/${alert.deviceId}`" class="text-blue-600 dark:text-blue-400 hover:underline font-medium">
                  {{ alert.deviceName || 'Unknown Device' }}
                </router-link>
              </div>
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Status</p>
                <span :class="['px-2 py-1 rounded text-sm font-medium inline-block', !alert.resolved && !alert.resolvedAt ? 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200' : 'bg-gray-200 dark:bg-gray-600 text-gray-800 dark:text-gray-200']">
                  {{ !alert.resolved && !alert.resolvedAt ? 'Active' : 'Resolved' }}
                </span>
              </div>
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Created</p>
                <p class="text-sm text-gray-900 dark:text-white">{{ formatDateTime(alert.timestamp || alert.createdAt) }}</p>
              </div>
            </div>

            <!-- Description -->
            <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
              <h3 class="font-semibold text-gray-900 dark:text-white mb-2">Description</h3>
              <p class="text-gray-700 dark:text-gray-300">{{ alert.description || alert.message || 'No additional details available' }}</p>
            </div>

            <!-- Resolved Information -->
            <div v-if="alert.resolved || alert.resolvedAt" class="border-t border-gray-200 dark:border-gray-700 pt-4 bg-green-50 dark:bg-green-900/20 p-4 rounded-lg">
              <h3 class="font-semibold text-green-900 dark:text-green-200 mb-2">Resolution</h3>
              <p class="text-sm text-green-800 dark:text-green-300">
                Resolved on {{ formatDateTime(alert.resolvedAt || new Date().toISOString()) }}
              </p>
            </div>
          </div>
        </div>

        <!-- Alert History -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
          <div class="px-6 py-4 border-b border-gray-200 dark:border-gray-700">
            <h3 class="font-semibold text-gray-900 dark:text-white">History</h3>
          </div>
          <div class="p-6">
            <div v-if="alertHistory.length === 0" class="text-center text-gray-500 dark:text-gray-400 py-8">
              No history available
            </div>
            <div v-else class="space-y-3">
              <div v-for="(event, idx) in alertHistory" :key="idx" class="flex gap-3 pb-3" :class="{ 'border-b border-gray-200 dark:border-gray-700': idx < alertHistory.length - 1 }">
                <div class="flex-shrink-0 w-2 h-2 bg-blue-600 rounded-full mt-2"></div>
                <div class="flex-1">
                  <p class="text-sm font-medium text-gray-900 dark:text-white">{{ event.action }}</p>
                  <p class="text-xs text-gray-600 dark:text-gray-400">{{ formatTime(event.timestamp) }}</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Sidebar Actions -->
      <div class="lg:col-span-1">
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 sticky top-20 space-y-4">
          <h3 class="font-semibold text-gray-900 dark:text-white">Actions</h3>
          
          <!-- Status Actions -->
          <div class="space-y-2">
            <button
              v-if="!alert.resolved && !alert.resolvedAt"
              @click="handleResolve"
              :disabled="isActioning"
              class="w-full px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition font-medium"
            >
              {{ isActioning ? 'Resolving...' : 'Resolve Alert' }}
            </button>
            <button
              v-else
              @click="handleReopen"
              :disabled="isActioning"
              class="w-full px-4 py-2 bg-orange-600 text-white rounded-lg hover:bg-orange-700 disabled:bg-gray-400 transition font-medium"
            >
              {{ isActioning ? 'Reopening...' : 'Reopen Alert' }}
            </button>
          </div>

          <!-- Delete Action -->
          <button
            @click="handleDelete"
            :disabled="isActioning"
            class="w-full px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:bg-gray-400 transition font-medium"
          >
            {{ isActioning ? 'Deleting...' : 'Delete Alert' }}
          </button>

          <!-- Refresh -->
          <button
            @click="loadAlert"
            :disabled="isLoading"
            class="w-full px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-900 dark:text-white rounded-lg hover:bg-gray-300 dark:hover:bg-gray-600 disabled:opacity-50 transition font-medium"
          >
            Refresh
          </button>

          <!-- Device Link -->
          <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
            <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2">Related Device</p>
            <router-link
              :to="`/devices/${alert.deviceId}`"
              class="block px-3 py-2 bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400 rounded-lg hover:bg-blue-100 dark:hover:bg-blue-900/30 text-center transition"
            >
              View {{ alert.deviceName || 'Device' }}
            </router-link>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAlerts } from '@/composables/useAlerts'
import toastService from '@services/toastService'
import type { HealthAlert } from '@types'

const router = useRouter()
const route = useRoute()

const { fetchAlertById, resolveAlert, reopenAlert, deleteAlert } = useAlerts()

const alert = ref<HealthAlert | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const isActioning = ref(false)
const isLoading = ref(false)

const alertId = computed(() => route.params.id as string)

const alertHistory = computed(() => {
  if (!alert.value) return []
  
  const events = [
    { action: 'Alert created', timestamp: alert.value.timestamp || alert.value.createdAt }
  ]
  
  if (alert.value.resolvedAt) {
    events.push({ action: 'Alert resolved', timestamp: alert.value.resolvedAt })
  }
  
  return events
})

const loadAlert = async () => {
  isLoading.value = true
  loading.value = true
  try {
    const data = await fetchAlertById(alertId.value)
    if (data) {
      alert.value = data
      error.value = null
    } else {
      alert.value = null
      error.value = 'Alert not found'
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to load alert'
    alert.value = null
  } finally {
    loading.value = false
    isLoading.value = false
  }
}

const handleResolve = async () => {
  if (!alert.value) return
  
  isActioning.value = true
  try {
    const success = await resolveAlert(alert.value.alertId)
    if (success) {
      alert.value.resolved = true
      alert.value.resolvedAt = new Date().toISOString()
      toastService.success('Alert resolved')
    } else {
      toastService.error('Failed to resolve alert')
    }
  } finally {
    isActioning.value = false
  }
}

const handleReopen = async () => {
  if (!alert.value) return
  
  isActioning.value = true
  try {
    const success = await reopenAlert(alert.value.alertId)
    if (success) {
      alert.value.resolved = false
      alert.value.resolvedAt = undefined
      toastService.success('Alert reopened')
    } else {
      toastService.error('Failed to reopen alert')
    }
  } finally {
    isActioning.value = false
  }
}

const handleDelete = async () => {
  if (!alert.value || !confirm('Are you sure you want to delete this alert?')) return
  
  isActioning.value = true
  try {
    const success = await deleteAlert(alert.value.alertId)
    if (success) {
      toastService.success('Alert deleted')
      router.push('/alerts')
    } else {
      toastService.error('Failed to delete alert')
    }
  } finally {
    isActioning.value = false
  }
}

const getSeverityBadgeClass = (sev: string) => {
  switch (sev) {
    case 'Critical':
      return 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200'
    case 'High':
      return 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200'
    case 'Medium':
      return 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200'
    case 'Low':
      return 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
    default:
      return 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
  }
}

const getSeverityBgClass = (sev: string) => {
  switch (sev) {
    case 'Critical':
      return 'bg-red-50 dark:bg-red-900/20'
    case 'High':
      return 'bg-orange-50 dark:bg-orange-900/20'
    case 'Medium':
      return 'bg-yellow-50 dark:bg-yellow-900/20'
    case 'Low':
      return 'bg-blue-50 dark:bg-blue-900/20'
    default:
      return 'bg-gray-50 dark:bg-gray-700'
  }
}

const formatTime = (dateString?: string | null): string => {
  if (!dateString) return 'Unknown'
  const date = new Date(dateString)
  const now = new Date()
  const diff = now.getTime() - date.getTime()
  const minutes = Math.floor(diff / 60000)
  
  if (minutes < 1) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`
  
  return date.toLocaleDateString()
}

const formatDateTime = (dateString?: string | null): string => {
  if (!dateString) return 'Unknown'
  const date = new Date(dateString)
  return date.toLocaleString()
}

onMounted(() => {
  loadAlert()
})
</script>
