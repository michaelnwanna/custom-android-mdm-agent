<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Policies</h1>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1">
          {{ policyStore.policies.length }} total · {{ policyStore.activePolicies.length }} active
        </p>
      </div>
      <div class="flex gap-3">
        <button
          @click="refreshPolicies"
          class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
          </svg>
          Refresh
        </button>
        <router-link
          to="/policies/new"
          class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          New Policy
        </router-link>
      </div>
    </div>

    <!-- Filters & Search -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
      <div class="grid grid-cols-1 md:grid-cols-4 gap-4">
        <!-- Search -->
        <div class="md:col-span-2">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Search</label>
          <input
            v-model="searchQuery"
            type="text"
            placeholder="Search by policy name or description..."
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Status Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Status</label>
          <select
            v-model="selectedStatus"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="all">All Policies</option>
            <option value="Active">Active</option>
            <option value="Draft">Draft</option>
            <option value="Archived">Archived</option>
          </select>
        </div>

        <!-- Enforcement Level Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Enforcement</label>
          <select
            v-model="selectedEnforcement"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="all">All Levels</option>
            <option value="Strict">Strict</option>
            <option value="Standard">Standard</option>
            <option value="Permissive">Permissive</option>
          </select>
        </div>
      </div>

      <!-- Bulk Actions -->
      <div v-if="selectedPolicies.length > 0" class="flex items-center justify-between border-t border-gray-200 dark:border-gray-700 pt-4">
        <span class="text-sm text-gray-600 dark:text-gray-400">{{ selectedPolicies.length }} selected</span>
        <div class="flex gap-2">
          <button
            @click="bulkActivate"
            :disabled="activatingBulk"
            class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition flex items-center gap-2 text-sm"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
            </svg>
            Activate
          </button>
          <button
            @click="bulkDeactivate"
            :disabled="activatingBulk"
            class="px-4 py-2 bg-yellow-600 text-white rounded-lg hover:bg-yellow-700 disabled:bg-gray-400 transition flex items-center gap-2 text-sm"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
            Deactivate
          </button>
        </div>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="policyStore.loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading policies...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="policyStore.error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 flex items-start gap-3">
      <svg class="w-5 h-5 text-red-600 dark:text-red-400 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
        <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
      </svg>
      <div>
        <h3 class="font-medium text-red-900 dark:text-red-200">Error loading policies</h3>
        <p class="text-sm text-red-700 dark:text-red-300 mt-1">{{ policyStore.error }}</p>
      </div>
    </div>

    <!-- Empty State -->
    <div v-else-if="filteredPolicies.length === 0" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">No policies found</h3>
      <p class="text-gray-600 dark:text-gray-400">
        {{ searchQuery || selectedStatus !== 'all' || selectedEnforcement !== 'all'
          ? 'Try adjusting your filters'
          : 'Create your first policy to get started' }}
      </p>
    </div>

    <!-- Policies Grid -->
    <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
      <div
        v-for="policy in paginatedPolicies"
        :key="policy.policyId"
        class="bg-white dark:bg-gray-800 rounded-lg shadow hover:shadow-lg transition relative"
      >
        <!-- Checkbox -->
        <div class="absolute top-4 left-4 z-10">
          <input
            type="checkbox"
            :checked="selectedPolicies.includes(policy.policyId)"
            @change="togglePolicy(policy.policyId)"
            class="rounded"
          />
        </div>

        <!-- Status Badge -->
        <div class="absolute top-4 right-4">
          <span :class="[
            'px-3 py-1 rounded-full text-xs font-medium inline-block',
            policy.status === 'Active' ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200' :
            policy.status === 'Draft' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
            'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
          ]">
            {{ policy.status }}
          </span>
        </div>

        <!-- Card Content -->
        <div class="p-6 pt-14">
          <!-- Title & Enforcement -->
          <div class="mb-4">
            <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-2">{{ policy.name }}</h3>
            <span :class="[
              'px-2 py-1 rounded text-xs font-medium inline-block',
              policy.enforcementLevel === 'Strict' ? 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200' :
              policy.enforcementLevel === 'Standard' ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200' :
              'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
            ]">
              {{ policy.enforcementLevel }}
            </span>
          </div>

          <!-- Description -->
          <p class="text-sm text-gray-600 dark:text-gray-400 mb-4 line-clamp-2">{{ policy.description }}</p>

          <!-- Device Count -->
          <div class="flex items-center gap-2 text-sm text-gray-500 dark:text-gray-400 mb-4">
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 18h.01M8 14h8m-8-4h8M9 9h6" />
            </svg>
            <span>{{ policy.deviceCount }} devices</span>
          </div>

          <!-- Rules Preview -->
          <div class="mb-4">
            <p class="text-xs font-semibold text-gray-700 dark:text-gray-300 mb-2">Rules ({{ policy.rules?.length || 0 }})</p>
            <div class="flex flex-wrap gap-1">
              <span
                v-for="(rule, idx) in (policy.rules || []).slice(0, 2)"
                :key="idx"
                class="px-2 py-1 bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 text-xs rounded"
              >
                {{ rule }}
              </span>
              <span
                v-if="(policy.rules || []).length > 2"
                class="px-2 py-1 bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400 text-xs rounded"
              >
                +{{ (policy.rules || []).length - 2 }} more
              </span>
            </div>
          </div>

          <!-- Modified Date -->
          <p class="text-xs text-gray-500 dark:text-gray-400 mb-4">Modified {{ formatTime(policy.updatedAt) }}</p>

          <!-- Actions -->
          <div class="flex gap-2">
            <router-link
              :to="`/policies/${policy.policyId}`"
              class="flex-1 px-3 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition text-sm font-medium text-center"
            >
              Edit
            </router-link>
            <button
              v-if="policy.status === 'Draft'"
              @click="activatePolicy(policy.policyId)"
              :disabled="activatingId === policy.policyId"
              class="px-3 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition text-sm font-medium"
            >
              <svg v-if="activatingId !== policy.policyId" class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
              </svg>
              <span v-else>...</span>
            </button>
            <button
              v-else
              @click="deactivatePolicy(policy.policyId)"
              :disabled="activatingId === policy.policyId"
              class="px-3 py-2 bg-yellow-600 text-white rounded-lg hover:bg-yellow-700 disabled:bg-gray-400 transition text-sm font-medium"
            >
              <svg v-if="activatingId !== policy.policyId" class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
              <span v-else>...</span>
            </button>
            <button
              @click="deletePolicy(policy.policyId)"
              :disabled="deletingId === policy.policyId"
              class="px-3 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:bg-gray-400 transition text-sm font-medium"
            >
              <svg v-if="deletingId !== policy.policyId" class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
              </svg>
              <span v-else>...</span>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Pagination -->
    <div v-if="filteredPolicies.length > 0" class="bg-white dark:bg-gray-800 border-t border-gray-200 dark:border-gray-700 px-6 py-4 rounded-b-lg flex items-center justify-between">
      <div class="flex items-center gap-2">
        <label class="text-sm text-gray-600 dark:text-gray-400">Items per page:</label>
        <select
          v-model.number="pageSize"
          class="px-2 py-1 border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-gray-700 text-gray-900 dark:text-white text-sm"
        >
          <option :value="6">6</option>
          <option :value="12">12</option>
          <option :value="24">24</option>
        </select>
      </div>

      <div class="text-sm text-gray-600 dark:text-gray-400">
        Showing {{ startIndex + 1 }}-{{ Math.min(endIndex, filteredPolicies.length) }} of {{ filteredPolicies.length }}
      </div>

      <div class="flex items-center gap-1">
        <button
          @click="currentPage = Math.max(1, currentPage - 1)"
          :disabled="currentPage === 1"
          class="px-3 py-1 border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 text-sm"
        >
          Previous
        </button>

        <div class="flex gap-1 mx-2">
          <template v-for="page in paginationRange" :key="page">
            <button
              v-if="page === '...'"
              class="px-2 py-1 text-gray-600 dark:text-gray-400"
            >
              ...
            </button>
            <button
              v-else
              @click="currentPage = page as number"
              :class="[
                'px-3 py-1 rounded text-sm font-medium',
                currentPage === page
                  ? 'bg-blue-600 text-white'
                  : 'border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700'
              ]"
            >
              {{ page }}
            </button>
          </template>
        </div>

        <button
          @click="currentPage = Math.min(totalPages, currentPage + 1)"
          :disabled="currentPage === totalPages"
          class="px-3 py-1 border border-gray-300 dark:border-gray-600 rounded hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 text-sm"
        >
          Next
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { usePolicyStore } from '@stores/policyStore'
import type { PolicyDetails } from '@stores/policyStore'

const policyStore = usePolicyStore()
const searchQuery = ref('')
const selectedStatus = ref('all')
const selectedEnforcement = ref('all')
const selectedPolicies = ref<string[]>([])
const currentPage = ref(1)
const pageSize = ref(6)
const activatingId = ref<string | null>(null)
const activatingBulk = ref(false)
const deletingId = ref<string | null>(null)

// Filtered policies based on search and filters
const filteredPolicies = computed(() => {
  let result = [...policyStore.policies]

  // Status filter
  if (selectedStatus.value !== 'all') {
    result = result.filter(p => p.status === selectedStatus.value)
  }

  // Enforcement filter
  if (selectedEnforcement.value !== 'all') {
    result = result.filter(p => p.enforcementLevel === selectedEnforcement.value)
  }

  // Search filter
  if (searchQuery.value) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(p =>
      p.name.toLowerCase().includes(query) ||
      p.description.toLowerCase().includes(query)
    )
  }

  return result
})

// Reset to page 1 when filters change
const _ = computed(() => {
  searchQuery.value
  selectedStatus.value
  selectedEnforcement.value
  currentPage.value = 1
  selectedPolicies.value = []
  return null
})

// Pagination info
const totalPages = computed(() => Math.ceil(filteredPolicies.value.length / pageSize.value))
const startIndex = computed(() => (currentPage.value - 1) * pageSize.value)
const endIndex = computed(() => startIndex.value + pageSize.value)

const paginatedPolicies = computed(() =>
  filteredPolicies.value.slice(startIndex.value, endIndex.value)
)

// Pagination range
const paginationRange = computed(() => {
  const pages: (number | string)[] = []
  const delta = 2
  const range = {
    start: Math.round(currentPage.value - delta),
    end: Math.round(currentPage.value + delta)
  }

  range.start = Math.max(range.start, 1)
  range.end = Math.min(range.end, totalPages.value)

  const includePageNumbers = (from: number, to: number) => {
    for (let i = from; i <= to; i++) {
      pages.push(i)
    }
  }

  if (range.start > 1) {
    includePageNumbers(1, 1)
    if (range.start > 2) pages.push('...')
  }

  includePageNumbers(range.start, range.end)

  if (range.end < totalPages.value) {
    if (range.end < totalPages.value - 1) pages.push('...')
    includePageNumbers(totalPages.value, totalPages.value)
  }

  return pages
})

// Methods
function togglePolicy(policyId: string) {
  const index = selectedPolicies.value.indexOf(policyId)
  if (index > -1) {
    selectedPolicies.value.splice(index, 1)
  } else {
    selectedPolicies.value.push(policyId)
  }
}

async function activatePolicy(policyId: string) {
  activatingId.value = policyId
  try {
    await policyStore.activatePolicy(policyId)
  } finally {
    activatingId.value = null
  }
}

async function deactivatePolicy(policyId: string) {
  activatingId.value = policyId
  try {
    await policyStore.deactivatePolicy(policyId)
  } finally {
    activatingId.value = null
  }
}

async function deletePolicy(policyId: string) {
  deletingId.value = policyId
  try {
    await policyStore.deletePolicy(policyId)
    selectedPolicies.value = selectedPolicies.value.filter(id => id !== policyId)
  } finally {
    deletingId.value = null
  }
}

async function bulkActivate() {
  activatingBulk.value = true
  try {
    await Promise.all(selectedPolicies.value.map(id => policyStore.activatePolicy(id)))
    selectedPolicies.value = []
  } finally {
    activatingBulk.value = false
  }
}

async function bulkDeactivate() {
  activatingBulk.value = true
  try {
    await Promise.all(selectedPolicies.value.map(id => policyStore.deactivatePolicy(id)))
    selectedPolicies.value = []
  } finally {
    activatingBulk.value = false
  }
}

function refreshPolicies() {
  policyStore.fetchPolicies()
}

function formatTime(timestamp: string): string {
  const date = new Date(timestamp)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffDays = Math.floor(diffMs / 86400000)

  if (diffDays === 0) return 'Today'
  if (diffDays === 1) return 'Yesterday'
  if (diffDays < 30) return `${diffDays}d ago`
  return date.toLocaleDateString()
}

onMounted(() => {
  policyStore.fetchPolicies()
})
</script>
