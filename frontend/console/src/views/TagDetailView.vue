<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center gap-4">
      <button
        @click="goBack"
        class="text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white"
      >
        ← Back to Tags
      </button>
      <h1 class="text-3xl font-bold text-gray-900 dark:text-white">
        {{ isNew ? 'Create Tag' : 'Edit Tag' }}
      </h1>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      <p class="mt-4 text-gray-600 dark:text-gray-400">Loading tag...</p>
    </div>

    <!-- Form -->
    <div v-else class="space-y-6">
      <!-- Basic Information -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-6">
        <h2 class="text-xl font-semibold text-gray-900 dark:text-white">Basic Information</h2>

        <!-- Name -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Tag Name *
          </label>
          <input
            v-model="editForm.name"
            type="text"
            placeholder="e.g., Production, High-Priority, Finance Department"
            class="w-full px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-500 dark:placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <p v-if="!editForm.name" class="text-red-500 text-sm mt-1">Tag name is required</p>
        </div>

        <!-- Description -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Description
          </label>
          <textarea
            v-model="editForm.description"
            placeholder="Describe the purpose of this tag and which devices should have it..."
            rows="4"
            class="w-full px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-500 dark:placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
          ></textarea>
        </div>

        <!-- Color Selection -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Tag Color
          </label>
          <div class="flex flex-wrap gap-3">
            <button
              v-for="color in tagStore.colors"
              :key="color"
              @click="editForm.color = color"
              :class="[
                'w-12 h-12 rounded-lg transition-transform',
                editForm.color === color ? 'ring-2 ring-offset-2 ring-blue-600 scale-110' : 'hover:scale-105'
              ]"
              :style="{ backgroundColor: color }"
            ></button>
          </div>
        </div>
      </div>

      <!-- Device Assignment (for existing tags) -->
      <div v-if="!isNew" class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-6">
        <h2 class="text-xl font-semibold text-gray-900 dark:text-white">
          Device Assignment ({{ editForm.deviceCount }} devices)
        </h2>

        <div class="flex flex-col md:flex-row gap-4">
          <!-- Assign Devices -->
          <div class="flex-1">
            <button
              @click="showAssignModal = true"
              class="w-full px-4 py-3 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors font-medium"
            >
              + Assign Devices
            </button>
          </div>

          <!-- Bulk Actions -->
          <div class="flex-1">
            <button
              @click="showUnassignModal = true"
              class="w-full px-4 py-3 bg-orange-600 text-white rounded-lg hover:bg-orange-700 transition-colors font-medium"
            >
              - Remove Devices
            </button>
          </div>
        </div>

        <div class="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4">
          <p class="text-sm text-blue-700 dark:text-blue-400">
            <strong>Note:</strong> To view and manage device assignments, go to the Devices page and use bulk tagging. Device counts are updated in real-time as devices are assigned or unassigned.
          </p>
        </div>
      </div>

      <!-- Actions -->
      <div class="flex gap-3 justify-end">
        <button
          @click="goBack"
          class="px-6 py-2 border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-300 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors font-medium"
        >
          Cancel
        </button>
        <button
          @click="deleteCurrentTag"
          v-if="!isNew"
          class="px-6 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors font-medium"
        >
          Delete Tag
        </button>
        <button
          @click="saveTag"
          :disabled="!editForm.name || saving"
          class="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors font-medium"
        >
          {{ saving ? 'Saving...' : 'Save Tag' }}
        </button>
      </div>
    </div>

    <!-- Assign Modal -->
    <div v-if="showAssignModal" class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg p-6 max-w-md w-full mx-4">
        <h3 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">Assign Devices</h3>
        <p class="text-gray-600 dark:text-gray-400 mb-4">
          Go to the Devices page and select multiple devices, then use the "Add Tags" bulk action to assign this tag.
        </p>
        <div class="flex justify-end">
          <button
            @click="showAssignModal = false"
            class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
          >
            OK
          </button>
        </div>
      </div>
    </div>

    <!-- Unassign Modal -->
    <div v-if="showUnassignModal" class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg p-6 max-w-md w-full mx-4">
        <h3 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">Remove Devices</h3>
        <p class="text-gray-600 dark:text-gray-400 mb-4">
          Go to the Devices page and select multiple devices with this tag, then use the "Remove Tags" bulk action.
        </p>
        <div class="flex justify-end">
          <button
            @click="showUnassignModal = false"
            class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
          >
            OK
          </button>
        </div>
      </div>
    </div>

    <!-- Delete Confirmation Modal -->
    <div v-if="showDeleteConfirm" class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg p-6 max-w-sm">
        <h3 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">Delete Tag?</h3>
        <p class="text-gray-600 dark:text-gray-400 mb-6">
          This tag will be removed from all {{ editForm.deviceCount }} assigned devices. This action cannot be undone.
        </p>
        <div class="flex gap-3 justify-end">
          <button
            @click="showDeleteConfirm = false"
            class="px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700"
          >
            Cancel
          </button>
          <button
            @click="performDelete"
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
import { ref, reactive, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useTagStore } from '@/stores/tagStore'
import type { Tag } from '@/types'

const router = useRouter()
const route = useRoute()
const tagStore = useTagStore()

// State
const loading = ref(false)
const saving = ref(false)
const showAssignModal = ref(false)
const showUnassignModal = ref(false)
const showDeleteConfirm = ref(false)

const editForm = reactive<Omit<Tag, 'tagId'> & { deviceCount: number }>({
  name: '',
  description: '',
  color: tagStore.colors[0],
  deviceCount: 0
})

const isNew = computed(() => route.params.id === 'new')

// Methods
onMounted(async () => {
  if (!isNew.value) {
    loading.value = true
    const tag = await tagStore.getTagById(route.params.id as string)
    if (tag) {
      editForm.name = tag.name
      editForm.description = tag.description
      editForm.color = tag.color
      editForm.deviceCount = tag.deviceCount
    }
    loading.value = false
  }
})

const saveTag = async () => {
  if (!editForm.name) return

  saving.value = true
  try {
    if (isNew.value) {
      const newTag = await tagStore.createTag({
        name: editForm.name,
        description: editForm.description,
        color: editForm.color,
        deviceCount: 0
      })
      router.push(`/tags/${newTag.tagId}`)
    } else {
      await tagStore.updateTag(route.params.id as string, {
        name: editForm.name,
        description: editForm.description,
        color: editForm.color
      })
    }
  } finally {
    saving.value = false
  }
}

const deleteCurrentTag = () => {
  showDeleteConfirm.value = true
}

const performDelete = async () => {
  await tagStore.deleteTag(route.params.id as string)
  showDeleteConfirm.value = false
  router.push('/tags')
}

const goBack = () => {
  router.push('/tags')
}
</script>
