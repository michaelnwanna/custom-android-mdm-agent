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
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Policy Details</h1>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1" v-if="policy">v{{ policy.version || 1 }} · {{ policy.type }}</p>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading policy...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
      <div class="flex items-start gap-3">
        <svg class="w-5 h-5 text-red-600 dark:text-red-400 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
        </svg>
        <div>
          <h3 class="font-medium text-red-900 dark:text-red-200">Error loading policy</h3>
          <p class="text-sm text-red-700 dark:text-red-300 mt-1">{{ error }}</p>
        </div>
      </div>
    </div>

    <!-- Policy Not Found -->
    <div v-else-if="!policy" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">Policy not found</h3>
      <p class="text-gray-600 dark:text-gray-400">This policy may have been deleted or is no longer available.</p>
    </div>

    <!-- Policy Details -->
    <div v-else class="grid grid-cols-1 lg:grid-cols-3 gap-6">
      <!-- Main Content -->
      <div class="lg:col-span-2 space-y-6">
        <!-- Policy Card -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
          <!-- Header -->
          <div :class="['px-6 py-4 border-b border-gray-200 dark:border-gray-700', getTypeBgClass(policy.type)]">
            <div class="flex items-start justify-between">
              <div>
                <h2 class="text-2xl font-bold">{{ policy.name }}</h2>
                <p class="text-sm mt-1">{{ policy.description }}</p>
              </div>
              <span :class="['px-4 py-2 rounded-full text-sm font-semibold', getStatusBadgeClass(policy.status)]">
                {{ policy.status || 'Draft' }}
              </span>
            </div>
          </div>

          <!-- Details -->
          <div class="p-6 space-y-4">
            <div class="grid grid-cols-2 gap-4">
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Policy ID</p>
                <p class="font-mono text-sm text-gray-900 dark:text-white break-all">{{ policy.policyId }}</p>
              </div>
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Type</p>
                <p class="text-sm text-gray-900 dark:text-white font-medium">{{ policy.type || 'Policy' }}</p>
              </div>
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Version</p>
                <p class="text-sm text-gray-900 dark:text-white font-medium">v{{ policy.version || 1 }}</p>
              </div>
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg">
                <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Created</p>
                <p class="text-sm text-gray-900 dark:text-white font-medium">{{ formatDate(policy.createdAt) }}</p>
              </div>
            </div>

            <!-- Deployment Stats -->
            <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
              <h3 class="font-semibold text-gray-900 dark:text-white mb-4">Deployment</h3>
              <div class="grid grid-cols-3 gap-4">
                <div class="bg-blue-50 dark:bg-blue-900/20 p-4 rounded-lg">
                  <p class="text-xs text-blue-600 dark:text-blue-400 font-medium">Devices</p>
                  <p class="text-2xl font-bold text-blue-900 dark:text-blue-200">{{ policy.deviceCount || 0 }}</p>
                </div>
                <div class="bg-green-50 dark:bg-green-900/20 p-4 rounded-lg">
                  <p class="text-xs text-green-600 dark:text-green-400 font-medium">Compliant</p>
                  <p class="text-2xl font-bold text-green-900 dark:text-green-200">{{ policy.compliantDeviceCount || 0 }}</p>
                </div>
                <div class="bg-purple-50 dark:bg-purple-900/20 p-4 rounded-lg">
                  <p class="text-xs text-purple-600 dark:text-purple-400 font-medium">Compliance</p>
                  <p :class="['text-2xl font-bold', getComplianceColorClass(policy.compliancePercentage)]">
                    {{ policy.compliancePercentage || 0 }}%
                  </p>
                </div>
              </div>

              <!-- Compliance Bar -->
              <div class="mt-4 w-full bg-gray-200 dark:bg-gray-600 rounded-full h-3">
                <div
                  :style="{ width: `${policy.compliancePercentage || 0}%` }"
                  :class="['h-full rounded-full transition-all', getComplianceBarClass(policy.compliancePercentage)]"
                />
              </div>
            </div>

            <!-- Configuration -->
            <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
              <h3 class="font-semibold text-gray-900 dark:text-white mb-3">Configuration</h3>
              <div class="bg-gray-50 dark:bg-gray-700/50 p-4 rounded-lg font-mono text-sm text-gray-800 dark:text-gray-200 max-h-64 overflow-y-auto">
                <pre>{{ JSON.stringify(policy.configuration || {}, null, 2) }}</pre>
              </div>
            </div>
          </div>
        </div>

        <!-- Version History -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
          <div class="px-6 py-4 border-b border-gray-200 dark:border-gray-700">
            <h3 class="font-semibold text-gray-900 dark:text-white">Version History</h3>
          </div>
          <div class="p-6">
            <div v-if="versionHistory.length === 0" class="text-center text-gray-500 dark:text-gray-400 py-8">
              No version history available
            </div>
            <div v-else class="space-y-2">
              <div v-for="(version, idx) in versionHistory" :key="idx" class="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-700/50 rounded-lg">
                <span class="font-medium text-gray-900 dark:text-white">v{{ version }}</span>
                <button
                  v-if="idx > 0"
                  @click="handleRollback(version)"
                  :disabled="isActioning"
                  class="px-3 py-1 text-sm bg-orange-600 text-white rounded hover:bg-orange-700 disabled:bg-gray-400 transition"
                >
                  Rollback
                </button>
                <span v-else class="text-xs text-gray-600 dark:text-gray-400">Current</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Sidebar Actions -->
      <div class="lg:col-span-1">
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 sticky top-20 space-y-4">
          <h3 class="font-semibold text-gray-900 dark:text-white">Actions</h3>

          <!-- Primary Actions -->
          <button
            @click="handleAssignPolicy"
            :disabled="isActioning"
            class="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 transition font-medium"
          >
            Assign Policy
          </button>

          <!-- Edit -->
          <router-link
            :to="`/policies/${policy.policyId}/edit`"
            class="block text-center px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-900 dark:text-white rounded-lg hover:bg-gray-300 dark:hover:bg-gray-600 transition font-medium"
          >
            Edit
          </router-link>

          <!-- Delete -->
          <button
            @click="handleDelete"
            :disabled="isActioning"
            class="w-full px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:bg-gray-400 transition font-medium"
          >
            {{ isActioning ? 'Deleting...' : 'Delete Policy' }}
          </button>

          <!-- Refresh -->
          <button
            @click="loadPolicy"
            :disabled="isLoading"
            class="w-full px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-900 dark:text-white rounded-lg hover:bg-gray-300 dark:hover:bg-gray-600 disabled:opacity-50 transition font-medium"
          >
            Refresh
          </button>

          <!-- Status -->
          <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
            <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2">Status</p>
            <p :class="['px-3 py-2 rounded-lg text-sm font-medium text-center', getStatusBadgeClass(policy.status)]">
              {{ policy.status || 'Draft' }}
            </p>
          </div>

          <!-- Compliance Indicator -->
          <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
            <p class="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2">Overall Compliance</p>
            <div class="w-full bg-gray-200 dark:bg-gray-600 rounded-full h-3 mb-2">
              <div
                :style="{ width: `${policy.compliancePercentage || 0}%` }"
                :class="['h-full rounded-full transition-all', getComplianceBarClass(policy.compliancePercentage)]"
              />
            </div>
            <p :class="['text-sm font-bold', getComplianceColorClass(policy.compliancePercentage)]">
              {{ policy.compliancePercentage || 0 }}% Compliant
            </p>
          </div>
        </div>
      </div>
    </div>

    <!-- Assign Policy Modal -->
    <AssignPolicyModal
      :is-open="showAssignModal"
      :policy="policy"
      @close="showAssignModal = false"
      @assign="handleAssignSubmit"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { usePolicies } from '@/composables/usePolicies'
import toastService from '@services/toastService'
import AssignPolicyModal from '@components/AssignPolicyModal.vue'
import type { Policy } from '@types'

const router = useRouter()
const route = useRoute()

const { fetchPolicyById, deletePolicy, rollbackPolicy } = usePolicies()

const policy = ref<Policy | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const isActioning = ref(false)
const isLoading = ref(false)
const showAssignModal = ref(false)

const policyId = computed(() => route.params.id as string)

const versionHistory = computed(() => {
  if (!policy.value) return []
  const versions = [policy.value.version || 1]
  
  // Add previous versions (mock)
  for (let i = 1; i < 3; i++) {
    const prevVersion = (policy.value.version || 1) - i
    if (prevVersion > 0) {
      versions.push(prevVersion)
    }
  }
  
  return versions
})

const loadPolicy = async () => {
  isLoading.value = true
  loading.value = true
  try {
    const data = await fetchPolicyById(policyId.value)
    if (data) {
      policy.value = data
      error.value = null
    } else {
      policy.value = null
      error.value = 'Policy not found'
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to load policy'
    policy.value = null
  } finally {
    loading.value = false
    isLoading.value = false
  }
}

const handleAssignPolicy = async () => {
  showAssignModal.value = true
}

const handleAssignSubmit = async (deviceIds: string[], groupId?: string) => {
  if (!policy.value) return
  
  isActioning.value = true
  try {
    // In mock mode, just show success
    const targetCount = groupId ? 5 : deviceIds.length // Mock: group has 5 devices
    toastService.success(`Policy assigned to ${targetCount} device(s)`)
    
    // In real implementation, call: await policyApi.assignPolicy(policy.value.policyId, deviceIds, groupId)
  } finally {
    isActioning.value = false
  }
}

const handleDelete = async () => {
  if (!policy.value || !confirm('Are you sure you want to delete this policy?')) return
  
  isActioning.value = true
  try {
    const success = await deletePolicy(policy.value.policyId)
    if (success) {
      toastService.success('Policy deleted')
      router.push('/policies')
    } else {
      toastService.error('Failed to delete policy')
    }
  } finally {
    isActioning.value = false
  }
}

const handleRollback = async (version: number) => {
  if (!policy.value) return
  if (!confirm(`Rollback to version ${version}?`)) return
  
  isActioning.value = true
  try {
    const success = await rollbackPolicy(policy.value.policyId, version)
    if (success) {
      toastService.success(`Rolled back to version ${version}`)
      await loadPolicy()
    } else {
      toastService.error('Failed to rollback policy')
    }
  } finally {
    isActioning.value = false
  }
}

const getTypeBgClass = (policyType: string) => {
  switch (policyType) {
    case 'Security':
      return 'bg-red-50 dark:bg-red-900/20'
    case 'Compliance':
      return 'bg-blue-50 dark:bg-blue-900/20'
    case 'Performance':
      return 'bg-purple-50 dark:bg-purple-900/20'
    default:
      return 'bg-gray-50 dark:bg-gray-700/50'
  }
}

const getStatusBadgeClass = (policyStatus: string) => {
  switch (policyStatus) {
    case 'Active':
      return 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200'
    case 'Draft':
      return 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200'
    case 'Archived':
      return 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
    default:
      return 'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
  }
}

const getComplianceColorClass = (compliance: number) => {
  if (compliance >= 90) return 'text-green-600 dark:text-green-400'
  if (compliance >= 70) return 'text-yellow-600 dark:text-yellow-400'
  return 'text-red-600 dark:text-red-400'
}

const getComplianceBarClass = (compliance: number) => {
  if (compliance >= 90) return 'bg-green-500'
  if (compliance >= 70) return 'bg-yellow-500'
  return 'bg-red-500'
}

const formatDate = (dateString: string): string => {
  if (!dateString) return 'N/A'
  const date = new Date(dateString)
  return date.toLocaleDateString()
}

onMounted(() => {
  loadPolicy()
})
</script>
