<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <h1 class="text-3xl font-bold text-gray-900 dark:text-white">Tags</h1>
      <RouterLink
        to="/tags/new"
        class="inline-flex items-center px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
      >
        <span class="text-lg mr-2">+</span>
        New Tag
      </RouterLink>
    </div>

    <!-- Search and Filters -->
    <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
      <div class="flex flex-col md:flex-row gap-4">
        <!-- Search -->
        <div class="flex-1">
          <input
            v-model="searchQuery"
            type="text"
            placeholder="Search tags by name or description..."
            class="w-full px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-500 dark:placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <!-- Sort -->
        <select
          v-model="sortBy"
          class="px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="devices">By Device Count</option>
          <option value="name">By Name (A-Z)</option>
          <option value="created">By Created Date</option>
        </select>
      </div>

      <!-- Filter by Device Count -->
      <div class="flex flex-wrap gap-2">
        <span class="text-sm text-gray-600 dark:text-gray-400 py-1">Filter by device count:</span>
        <button
          v-for="(range, key) in deviceCountRanges"
          :key="key"
          @click="selectedDeviceRange = key"
          :class="[
            'px-3 py-1 rounded-full text-sm transition-colors',
            selectedDeviceRange === key
              ? 'bg-blue-600 text-white'
              : 'bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-300 dark:hover:bg-gray-600'
          ]"
        >
          {{ range }}
        </button>
      </div>
    </div>

    <!-- Results Info -->
    <div class="text-sm text-gray-600 dark:text-gray-400">
      Showing {{ filteredAndSortedTags.length }} of {{ tagStore.tags.length }} tags
    </div>

    <!-- Tags Grid -->
    <div v-if="filteredAndSortedTags.length > 0" class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
      <div
        v-for="tag in paginatedTags"
        :key="tag.tagId"
        class="bg-white dark:bg-gray-800 rounded-lg shadow hover:shadow-lg transition-shadow p-6 cursor-pointer"
        @click="navigateToTag(tag.tagId)"
      >
        <!-- Color Indicator -->
        <div class="flex items-start justify-between mb-4">
          <div
            class="w-12 h-12 rounded-lg"
            :style="{ backgroundColor: tag.color, opacity: 0.2 }"
          ></div>
          <div class="flex gap-2">
            <button
              @click.stop="editTag(tag.tagId)"
              class="text-blue-600 dark:text-blue-400 hover:text-blue-700 text-sm"
            >
              Edit
            </button>
            <button
              @click.stop="confirmDelete(tag.tagId)"
              class="text-red-600 dark:text-red-400 hover:text-red-700 text-sm"
            >
              Delete
            </button>
          </div>
        </div>

        <!-- Tag Info -->
        <h3 class="text-lg font-semibold text-gray-900 dark:text-white mb-2">
          {{ tag.name }}
        </h3>
        <p class="text-gray-600 dark:text-gray-400 text-sm mb-4 line-clamp-2">
          {{ tag.description }}
        </p>

        <!-- Device Count Badge -->
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-2">
            <span
              class="inline-flex items-center px-3 py-1 rounded-full text-sm font-medium"
              :style="{
                backgroundColor: tag.color,
                color: 'white'
              }"
            >
              {{ tag.deviceCount }} device{{ tag.deviceCount !== 1 ? 's' : '' }}
            </span>
          </div>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <div v-else class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-12 h-12 mx-auto mb-4 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
      </svg>
      <h3 class="text-lg font-semibold text-gray-900 dark:text-white mb-2">No tags found</h3>
      <p class="text-gray-600 dark:text-gray-400 mb-4">
        {{ searchQuery ? 'Try adjusting your search criteria' : 'Create your first tag to organize devices' }}
      </p>
      <RouterLink
        to="/tags/new"
        class="inline-flex items-center px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
      >
        <span class="text-lg mr-2">+</span>
        Create Tag
      </RouterLink>
    </div>

    <!-- Pagination -->
    <div v-if="filteredAndSortedTags.length > pageSize" class="flex items-center justify-center gap-2">
      <button
        @click="currentPage = Math.max(1, currentPage - 1)"
        :disabled="currentPage === 1"
        class="px-3 py-1 rounded border border-gray-300 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
      >
        ← Prev
      </button>

      <template v-for="page in paginationRange" :key="page">
        <button
          v-if="page === '...'"
          class="px-2 py-1 text-gray-600 dark:text-gray-400 cursor-default"
        >
          ...
        </button>
        <button
          v-else
          @click="currentPage = page as number"
          :class="[
            'px-3 py-1 rounded transition-colors',
            currentPage === page
              ? 'bg-blue-600 text-white'
              : 'border border-gray-300 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700'
          ]"
        >
          {{ page }}
        </button>
      </template>

      <button
        @click="currentPage = Math.min(totalPages, currentPage + 1)"
        :disabled="currentPage === totalPages"
        class="px-3 py-1 rounded border border-gray-300 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
      >
        Next →
      </button>
    </div>

    <!-- Delete Confirmation Modal -->
    <div v-if="showDeleteConfirm" class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg p-6 max-w-sm">
        <h3 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">Delete Tag?</h3>
        <p class="text-gray-600 dark:text-gray-400 mb-6">
          This tag will be removed from all devices. This action cannot be undone.
        </p>
        <div class="flex gap-3 justify-end">
          <button
            @click="showDeleteConfirm = false"
            class="px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700"
          >
            Cancel
          </button>
          <button
            @click="deleteTag"
            class="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
          >
            Delete
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useTagStore } from '@/stores/tagStore'

const router = useRouter()
const tagStore = useTagStore()

// State
const searchQuery = ref('')
const sortBy = ref('devices')
const selectedDeviceRange = ref('all')
const currentPage = ref(1)
const pageSize = ref(9)
const showDeleteConfirm = ref(false)
const tagToDelete = ref<string | null>(null)

// Device count ranges
const deviceCountRanges: Record<string, string> = {
  all: 'All',
  high: '50+ Devices',
  medium: '20-49 Devices',
  low: '1-19 Devices'
}

// Computed
const filteredAndSortedTags = computed(() => {
  let result = tagStore.tags.filter(tag => {
    // Search filter
    const matchesSearch = !searchQuery.value || 
      tag.name.toLowerCase().includes(searchQuery.value.toLowerCase()) ||
      tag.description.toLowerCase().includes(searchQuery.value.toLowerCase())
    
    // Device count filter
    let matchesDeviceRange = true
    if (selectedDeviceRange.value !== 'all') {
      if (selectedDeviceRange.value === 'high') {
        matchesDeviceRange = tag.deviceCount >= 50
      } else if (selectedDeviceRange.value === 'medium') {
        matchesDeviceRange = tag.deviceCount >= 20 && tag.deviceCount < 50
      } else if (selectedDeviceRange.value === 'low') {
        matchesDeviceRange = tag.deviceCount < 20
      }
    }

    return matchesSearch && matchesDeviceRange
  })

  // Sort
  if (sortBy.value === 'name') {
    result.sort((a, b) => a.name.localeCompare(b.name))
  } else if (sortBy.value === 'created') {
    // Mock: sort by tagId (simulates creation order)
    result.sort((a, b) => a.tagId.localeCompare(b.tagId))
  } else {
    // Default: by device count
    result.sort((a, b) => b.deviceCount - a.deviceCount)
  }

  return result
})

const totalPages = computed(() => {
  return Math.ceil(filteredAndSortedTags.value.length / pageSize.value)
})

const paginatedTags = computed(() => {
  const start = (currentPage.value - 1) * pageSize.value
  const end = start + pageSize.value
  return filteredAndSortedTags.value.slice(start, end)
})

const paginationRange = computed(() => {
  const range: (number | string)[] = []
  const delta = 2

  for (let i = Math.max(1, currentPage.value - delta); i <= Math.min(totalPages.value, currentPage.value + delta); i++) {
    range.push(i)
  }

  if (range[0] !== 1) {
    if (range[0] !== 2) range.unshift('...')
    range.unshift(1)
  }

  if (range[range.length - 1] !== totalPages.value) {
    if (range[range.length - 1] !== totalPages.value - 1) range.push('...')
    range.push(totalPages.value)
  }

  return range
})

// Methods
onMounted(() => {
  tagStore.fetchTags()
})

const navigateToTag = (tagId: string) => {
  router.push(`/tags/${tagId}`)
}

const editTag = (tagId: string) => {
  router.push(`/tags/${tagId}`)
}

const confirmDelete = (tagId: string) => {
  tagToDelete.value = tagId
  showDeleteConfirm.value = true
}

const deleteTag = async () => {
  if (tagToDelete.value) {
    await tagStore.deleteTag(tagToDelete.value)
    tagToDelete.value = null
    showDeleteConfirm.value = false
  }
}
</script>
