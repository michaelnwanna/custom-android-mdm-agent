<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Policies</h1>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1">
          {{ totalPolicies }} total · {{ averageCompliance }}% avg compliance · {{ displayText }}
        </p>
      </div>
      <div class="flex gap-2">
        <router-link
          to="/policies/new"
          class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          New Policy
        </router-link>
        <button
          @click="refreshPolicies"
          class="px-4 py-2 bg-gray-200 dark:bg-gray-700 text-gray-900 dark:text-white rounded-lg hover:bg-gray-300 dark:hover:bg-gray-600 disabled:bg-gray-400 transition flex items-center gap-2"
          :disabled="loading"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
          </svg>
          {{ loading ? 'Refreshing...' : 'Refresh' }}
        </button>
      </div>
    </div>

    <!-- Filters & Search -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
      <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
        <!-- Search -->
        <div class="md:col-span-2">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Search</label>
          <input
            :value="search"
            @input="updateSearch($event.target.value)"
            type="text"
            placeholder="Search by policy name..."
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Status Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Status</label>
          <select
            :value="status"
            @change="updateStatus($event.target.value)"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="">All Statuses</option>
            <option value="Active">Active</option>
            <option value="Draft">Draft</option>
            <option value="Archived">Archived</option>
          </select>
        </div>

        <!-- Type Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Type</label>
          <select
            :value="type"
            @change="updateType($event.target.value)"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="">All Types</option>
            <option value="Security">Security</option>
            <option value="Compliance">Compliance</option>
            <option value="Performance">Performance</option>
            <option value="Custom">Custom</option>
          </select>
        </div>
      </div>

      <!-- Clear Filters & Bulk Actions -->
      <div class="flex items-center justify-between border-t border-gray-200 dark:border-gray-700 pt-4">
        <button
          v-if="isFilterActive"
          @click="clearFilters"
          class="px-3 py-1 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white"
        >
          Clear Filters
        </button>
        <div v-if="selectedPolicies.length > 0" class="ml-auto flex items-center gap-2">
          <span class="text-sm text-gray-600 dark:text-gray-400">{{ selectedPolicies.length }} selected</span>
          <button
            @click="handleArchiveBulk"
            :disabled="isActioning"
            class="px-4 py-2 bg-orange-600 text-white rounded-lg hover:bg-orange-700 disabled:bg-gray-400 transition flex items-center gap-2 text-sm"
          >
            Archive Selected
          </button>
        </div>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading policies...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
      <div class="flex items-start gap-3">
        <svg class="w-5 h-5 text-red-600 dark:text-red-400 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
        </svg>
        <div>
          <h3 class="font-medium text-red-900 dark:text-red-200">Error loading policies</h3>
          <p class="text-sm text-red-700 dark:text-red-300 mt-1">{{ error }}</p>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <div v-else-if="policies.length === 0" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">No policies found</h3>
      <p class="text-gray-600 dark:text-gray-400 mb-4">{{ isFilterActive ? 'Try adjusting your filters' : 'Create your first policy to get started' }}</p>
      <router-link
        to="/policies/new"
        class="inline-flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition"
      >
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
        </svg>
        Create Policy
      </router-link>
    </div>

    <!-- Policies Grid -->
    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
      <div
        v-for="policy in policies"
        :key="policy.policyId"
        class="bg-white dark:bg-gray-800 rounded-lg shadow hover:shadow-lg transition cursor-pointer"
        @click="selectPolicy(policy.policyId)"
      >
        <!-- Card Header -->
        <div :class="['px-6 py-4 border-b border-gray-200 dark:border-gray-700 flex items-start justify-between', getTypeBgClass(policy.type)]">
          <div class="flex-1">
            <h3 class="text-lg font-semibold text-gray-900 dark:text-white">{{ policy.name }}</h3>
            <p class="text-sm mt-1">{{ policy.type || 'Policy' }}</p>
          </div>
          <input
            type="checkbox"
            :checked="selectedPolicies.includes(policy.policyId)"
            @click.stop="togglePolicy(policy.policyId)"
            class="rounded"
          />
        </div>

        <!-- Card Body -->
        <div class="p-6 space-y-4">
          <!-- Status Badge -->
          <div class="flex items-center justify-between">
            <span :class="['px-3 py-1 rounded-full text-xs font-medium', getStatusBadgeClass(policy.status)]">
              {{ policy.status || 'Draft' }}
            </span>
            <span class="text-xs text-gray-600 dark:text-gray-400">v{{ policy.version || 1 }}</span>
          </div>

          <!-- Description -->
          <p class="text-sm text-gray-600 dark:text-gray-400 line-clamp-2">{{ policy.description || 'No description' }}</p>

          <!-- Stats -->
          <div class="grid grid-cols-2 gap-2 bg-gray-50 dark:bg-gray-700/50 p-3 rounded">
            <div>
              <p class="text-xs text-gray-600 dark:text-gray-400">Devices</p>
              <p class="text-lg font-bold text-gray-900 dark:text-white">{{ policy.deviceCount || 0 }}</p>
            </div>
            <div>
              <p class="text-xs text-gray-600 dark:text-gray-400">Compliance</p>
              <p :class="['text-lg font-bold', getComplianceColorClass(policy.compliancePercentage)]">
                {{ policy.compliancePercentage || 0 }}%
              </p>
            </div>
          </div>

          <!-- Compliance Bar -->
          <div class="w-full bg-gray-200 dark:bg-gray-600 rounded-full h-2">
            <div
              :style="{ width: `${policy.compliancePercentage || 0}%` }"
              :class="['h-full rounded-full transition-all', getComplianceBarClass(policy.compliancePercentage)]"
            />
          </div>

          <!-- Actions -->
          <div class="flex gap-2 border-t border-gray-200 dark:border-gray-700 pt-4">
            <router-link
              :to="`/policies/${policy.policyId}`"
              class="flex-1 px-3 py-2 text-sm bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400 rounded hover:bg-blue-100 dark:hover:bg-blue-900/30 text-center transition font-medium"
              @click.stop
            >
              View
            </router-link>
            <button
              @click.stop="handleDeletePolicy(policy.policyId)"
              class="flex-1 px-3 py-2 text-sm bg-red-50 dark:bg-red-900/20 text-red-600 dark:text-red-400 rounded hover:bg-red-100 dark:hover:bg-red-900/30 transition font-medium"
            >
              Delete
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Pagination -->
    <div v-if="policies.length > 0 && totalPages > 1" class="flex items-center justify-between bg-white dark:bg-gray-800 rounded-lg shadow p-4">
      <button
        @click="prevPage"
        :disabled="pageNumber === 1"
        class="px-3 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 transition"
      >
        Previous
      </button>
      <div class="flex gap-1">
        <button
          v-for="page in pageRange"
          :key="page"
          @click="goToPage(typeof page === 'number' ? page : pageNumber)"
          :disabled="typeof page === 'string'"
          :class="[
            'px-3 py-1 text-sm rounded transition',
            page === pageNumber
              ? 'bg-blue-600 text-white'
              : 'border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700',
            typeof page === 'string' ? 'cursor-default' : 'cursor-pointer'
          ]"
        >
          {{ page }}
        </button>
      </div>
      <button
        @click="nextPage"
        :disabled="pageNumber === totalPages"
        class="px-3 py-1 text-sm border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 transition"
      >
        Next
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { usePolicies } from '@/composables/usePolicies'
import toastService from '@services/toastService'
import signalRService from '@services/signalRService'
import type { Policy } from '@types'

const router = useRouter()

const {
  policies,
  loading,
  error,
  type,
  status,
  search,
  pageNumber,
  totalPages,
  pageRange,
  totalPolicies,
  averageCompliance,
  displayText,
  fetchPolicies,
  goToPage,
  nextPage,
  prevPage,
  updateSearch,
  updateStatus,
  updateType,
  clearFilters,
  deletePolicy
} = usePolicies({ pageSize: 12, autoLoad: true })

const selectedPolicies = ref<string[]>([])
const isActioning = ref(false)

const isFilterActive = computed(() => search.value || status.value || type.value)

const refreshPolicies = async () => {
  await fetchPolicies()
}

const selectPolicy = (policyId: string) => {
  router.push(`/policies/${policyId}`)
}

const togglePolicy = (policyId: string) => {
  const index = selectedPolicies.value.indexOf(policyId)
  if (index > -1) {
    selectedPolicies.value.splice(index, 1)
  } else {
    selectedPolicies.value.push(policyId)
  }
}

const handleArchiveBulk = async () => {
  if (selectedPolicies.value.length === 0) return
  if (!confirm(`Archive ${selectedPolicies.value.length} policy/policies?`)) return
  
  isActioning.value = true
  try {
    let archived = 0
    for (const policyId of selectedPolicies.value) {
      const success = await deletePolicy(policyId)
      if (success) archived++
    }
    toastService.success(`${archived} policy/policies archived`)
    selectedPolicies.value = []
    await refreshPolicies()
  } finally {
    isActioning.value = false
  }
}

const handleDeletePolicy = async (policyId: string) => {
  if (!confirm('Delete this policy?')) return
  try {
    const success = await deletePolicy(policyId)
    if (success) {
      toastService.success('Policy deleted')
      selectedPolicies.value = selectedPolicies.value.filter(p => p !== policyId)
      await refreshPolicies()
    } else {
      toastService.error('Failed to delete policy')
    }
  } catch (err) {
    toastService.error('Delete operation failed')
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
    case 'Custom':
      return 'bg-gray-50 dark:bg-gray-700/50'
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

onMounted(async () => {
  await signalRService.connect({
    onPolicyUpdated: (policy: Policy) => {
      // Update policy in the list
      const idx = policies.value.findIndex(p => p.policyId === policy.policyId)
      if (idx !== -1) {
        policies.value[idx] = policy
      } else {
        policies.value.unshift(policy)
      }
      toastService.info(`Policy "${policy.name}" updated`)
    },
    onPolicyDeployed: (policyId: string, deviceIds: string[]) => {
      toastService.success(`Policy deployed to ${deviceIds.length} device(s)`)
      refreshPolicies()
    },
    onPolicyComplianceChanged: (policyId: string, compliancePercentage: number) => {
      const policy = policies.value.find(p => p.policyId === policyId)
      if (policy) {
        policy.compliancePercentage = compliancePercentage
      }
    }
  })

  // Subscribe to real-time policy updates
  await signalRService.subscribeToPolicies()
})
</script>
