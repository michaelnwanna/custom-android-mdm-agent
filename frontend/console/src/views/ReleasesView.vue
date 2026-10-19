<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Releases</h1>
        <p class="text-sm text-gray-600 dark:text-gray-400 mt-1">
          {{ releaseStore.releases.length }} total · {{ releaseStore.publishedReleases.length }} published
        </p>
      </div>
      <div class="flex gap-3">
        <button
          @click="refreshReleases"
          class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
          </svg>
          Refresh
        </button>
        <router-link
          to="/releases/new"
          class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          New Release
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
            placeholder="Search by app name or version..."
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
            <option value="all">All Releases</option>
            <option value="Published">Published</option>
            <option value="Draft">Draft</option>
            <option value="Deprecated">Deprecated</option>
          </select>
        </div>

        <!-- Priority Filter -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Priority</label>
          <select
            v-model="selectedPriority"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="all">All Priorities</option>
            <option value="Critical">Critical</option>
            <option value="High">High</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="releaseStore.loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading releases...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="releaseStore.error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 flex items-start gap-3">
      <svg class="w-5 h-5 text-red-600 dark:text-red-400 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
        <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
      </svg>
      <div>
        <h3 class="font-medium text-red-900 dark:text-red-200">Error loading releases</h3>
        <p class="text-sm text-red-700 dark:text-red-300 mt-1">{{ releaseStore.error }}</p>
      </div>
    </div>

    <!-- Empty State -->
    <div v-else-if="filteredReleases.length === 0" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M9 19l3 3m0 0l3-3m-3 3v-6" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">No releases found</h3>
      <p class="text-gray-600 dark:text-gray-400">
        {{ searchQuery || selectedStatus !== 'all' || selectedPriority !== 'all'
          ? 'Try adjusting your filters'
          : 'Create your first release to get started' }}
      </p>
    </div>

    <!-- Releases Table -->
    <div v-else class="bg-white dark:bg-gray-800 rounded-lg shadow overflow-hidden">
      <table class="w-full">
        <thead class="bg-gray-50 dark:bg-gray-700 border-b border-gray-200 dark:border-gray-600">
          <tr>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">App / Version</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Priority</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Status / Deployment</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Size / Downloads</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Installations</th>
            <th class="px-6 py-3 text-left text-sm font-medium text-gray-700 dark:text-gray-300">Released</th>
            <th class="px-6 py-3 text-right text-sm font-medium text-gray-700 dark:text-gray-300">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-200 dark:divide-gray-700">
          <tr
            v-for="release in paginatedReleases"
            :key="release.releaseId"
            class="hover:bg-gray-50 dark:hover:bg-gray-700/50 transition"
          >
            <td class="px-6 py-4">
              <div class="flex flex-col">
                <span class="font-medium text-gray-900 dark:text-white">{{ getAppName(release.packageName) }}</span>
                <span class="text-sm text-gray-600 dark:text-gray-400">v{{ release.versionName }}</span>
              </div>
            </td>
            <td class="px-6 py-4">
              <span :class="[
                'px-2 py-1 rounded text-xs font-medium',
                release.priority === 'Critical' ? 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200' :
                release.priority === 'High' ? 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200' :
                release.priority === 'Medium' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
                'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200'
              ]">
                {{ release.priority }}
              </span>
            </td>
            <td class="px-6 py-4">
              <div class="flex flex-col gap-1">
                <span :class="[
                  'px-2 py-1 rounded text-xs font-medium w-fit',
                  release.status === 'Published' ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200' :
                  release.status === 'Draft' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
                  'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
                ]">
                  {{ release.status }}
                </span>
                <span :class="[
                  'px-2 py-1 rounded text-xs font-medium w-fit',
                  release.deploymentStatus === 'Completed' ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200' :
                  release.deploymentStatus === 'In Progress' ? 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200' :
                  release.deploymentStatus === 'Failed' ? 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200' :
                  'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
                ]">
                  {{ release.deploymentStatus }}
                </span>
              </div>
            </td>
            <td class="px-6 py-4 text-sm text-gray-600 dark:text-gray-400">
              {{ release.fileSize }} MB / {{ release.downloadCount }} DL
            </td>
            <td class="px-6 py-4">
              <div class="flex flex-col">
                <span class="font-medium text-gray-900 dark:text-white">{{ release.installationCount }}</span>
                <span class="text-xs text-gray-500 dark:text-gray-400">/ {{ release.targetDeviceCount }}</span>
              </div>
            </td>
            <td class="px-6 py-4 text-sm text-gray-600 dark:text-gray-400">
              {{ formatTime(release.releaseDate) }}
            </td>
            <td class="px-6 py-4 text-right">
              <div class="flex justify-end gap-2">
                <router-link
                  :to="`/releases/${release.releaseId}`"
                  class="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300 text-sm font-medium"
                >
                  View
                </router-link>
                <button
                  v-if="release.status === 'Draft'"
                  @click="publishRelease(release.releaseId)"
                  :disabled="publishingId === release.releaseId"
                  class="text-green-600 dark:text-green-400 hover:text-green-800 dark:hover:text-green-300 text-sm font-medium disabled:opacity-50"
                >
                  {{ publishingId === release.releaseId ? 'Publishing...' : 'Publish' }}
                </button>
                <button
                  v-else-if="release.status === 'Published'"
                  @click="deprecateRelease(release.releaseId)"
                  :disabled="publishingId === release.releaseId"
                  class="text-yellow-600 dark:text-yellow-400 hover:text-yellow-800 dark:hover:text-yellow-300 text-sm font-medium disabled:opacity-50"
                >
                  {{ publishingId === release.releaseId ? 'Deprecating...' : 'Deprecate' }}
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>

      <!-- Pagination -->
      <div class="bg-white dark:bg-gray-800 border-t border-gray-200 dark:border-gray-700 px-6 py-4 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <label class="text-sm text-gray-600 dark:text-gray-400">Items per page:</label>
          <select
            v-model.number="pageSize"
            class="px-2 py-1 border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-gray-700 text-gray-900 dark:text-white text-sm"
          >
            <option :value="10">10</option>
            <option :value="20">20</option>
            <option :value="50">50</option>
          </select>
        </div>

        <div class="text-sm text-gray-600 dark:text-gray-400">
          Showing {{ startIndex + 1 }}-{{ Math.min(endIndex, filteredReleases.length) }} of {{ filteredReleases.length }}
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
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useReleaseStore } from '@stores/releaseStore'

const releaseStore = useReleaseStore()
const searchQuery = ref('')
const selectedStatus = ref('all')
const selectedPriority = ref('all')
const currentPage = ref(1)
const pageSize = ref(10)
const publishingId = ref<string | null>(null)

// Filtered releases based on search and filters
const filteredReleases = computed(() => {
  let result = [...releaseStore.releases]

  // Status filter
  if (selectedStatus.value !== 'all') {
    result = result.filter(r => r.status === selectedStatus.value)
  }

  // Priority filter
  if (selectedPriority.value !== 'all') {
    result = result.filter(r => r.priority === selectedPriority.value)
  }

  // Search filter
  if (searchQuery.value) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(r =>
      getAppName(r.packageName).toLowerCase().includes(query) ||
      r.versionName.toLowerCase().includes(query)
    )
  }

  return result.sort((a, b) => new Date(b.releaseDate).getTime() - new Date(a.releaseDate).getTime())
})

watch([searchQuery, selectedStatus, selectedPriority], () => {
  currentPage.value = 1
})

// Pagination info
const totalPages = computed(() => Math.ceil(filteredReleases.value.length / pageSize.value))
const startIndex = computed(() => (currentPage.value - 1) * pageSize.value)
const endIndex = computed(() => startIndex.value + pageSize.value)

const paginatedReleases = computed(() =>
  filteredReleases.value.slice(startIndex.value, endIndex.value)
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
function getAppName(packageName: string): string {
  const parts = packageName.split('.')
  return parts[parts.length - 1]
    .split('-')
    .map(w => w.charAt(0).toUpperCase() + w.slice(1))
    .join(' ')
}

async function publishRelease(releaseId: string) {
  publishingId.value = releaseId
  try {
    await releaseStore.publishRelease(releaseId)
  } finally {
    publishingId.value = null
  }
}

async function deprecateRelease(releaseId: string) {
  publishingId.value = releaseId
  try {
    await releaseStore.deprecateRelease(releaseId)
  } finally {
    publishingId.value = null
  }
}

function refreshReleases() {
  releaseStore.fetchReleases()
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
  releaseStore.fetchReleases()
})
</script>
