<template>
  <!-- Modal Backdrop -->
  <transition name="fade">
    <div
      v-if="isOpen"
      class="fixed inset-0 bg-black bg-opacity-50 z-40 flex items-center justify-center p-4"
      @click="closeModal"
    >
      <!-- Modal -->
      <div
        class="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-3xl w-full max-h-[90vh] overflow-hidden flex flex-col"
        @click.stop
      >
        <!-- Header -->
        <div class="bg-gradient-to-r from-purple-500 to-purple-600 dark:from-purple-700 dark:to-purple-800 px-6 py-4 flex items-center justify-between">
          <h2 class="text-xl font-bold text-white">Bulk Assign Policy</h2>
          <button
            @click="closeModal"
            :disabled="isAssigning"
            class="text-white hover:bg-white hover:bg-opacity-20 p-1 rounded transition disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-6 space-y-6">
          <!-- Policy Info -->
          <div v-if="policy" class="bg-purple-50 dark:bg-purple-900/20 p-4 rounded-lg border border-purple-200 dark:border-purple-800">
            <h3 class="font-semibold text-gray-900 dark:text-white mb-1">{{ policy.name }}</h3>
            <p class="text-sm text-gray-600 dark:text-gray-400">{{ policy.description }}</p>
            <div class="flex gap-4 mt-2 text-xs">
              <span class="text-gray-600 dark:text-gray-400"><strong>Type:</strong> {{ policy.type }}</span>
              <span class="text-gray-600 dark:text-gray-400"><strong>Version:</strong> v{{ policy.version || 1 }}</span>
            </div>
          </div>

          <!-- Assignment Info -->
          <div class="bg-blue-50 dark:bg-blue-900/20 p-4 rounded-lg border border-blue-200 dark:border-blue-800">
            <h3 class="font-semibold text-gray-900 dark:text-white mb-2">Bulk Assignment</h3>
            <p class="text-sm text-gray-600 dark:text-gray-400 mb-3">
              Select device groups to assign this policy to multiple devices at once. You can select multiple groups to reach a larger audience.
            </p>
            <div class="grid grid-cols-3 gap-3 text-sm">
              <div>
                <span class="text-gray-600 dark:text-gray-400">Groups Selected:</span>
                <div class="text-xl font-bold text-blue-600 dark:text-blue-400">{{ selectedGroups.length }}</div>
              </div>
              <div>
                <span class="text-gray-600 dark:text-gray-400">Devices Affected:</span>
                <div class="text-xl font-bold text-purple-600 dark:text-purple-400">{{ totalAffectedDevices }}</div>
              </div>
              <div>
                <span class="text-gray-600 dark:text-gray-400">Deployment Duration:</span>
                <div class="text-xl font-bold text-amber-600 dark:text-amber-400">~{{ estimatedDuration }}s</div>
              </div>
            </div>
          </div>

          <!-- Device Group Selector -->
          <div class="border-t border-gray-200 dark:border-gray-700 pt-6">
            <DeviceGroupSelector v-model="selectedGroups" />
          </div>
        </div>

        <!-- Footer -->
        <div class="bg-gray-50 dark:bg-gray-700/50 px-6 py-4 border-t border-gray-200 dark:border-gray-700 flex gap-3 justify-end">
          <button
            @click="closeModal"
            :disabled="isAssigning"
            class="px-4 py-2 border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-300 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-700 transition font-medium disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Cancel
          </button>
          <button
            @click="handleBulkAssign"
            :disabled="isAssigning || selectedGroups.length === 0"
            class="px-4 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition font-medium flex items-center gap-2"
          >
            <svg v-if="isAssigning" class="w-4 h-4 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
            {{ isAssigning ? 'Assigning...' : 'Assign to Selected Groups' }}
          </button>
        </div>
      </div>
    </div>
  </transition>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import DeviceGroupSelector from '@components/DeviceGroupSelector.vue'
import type { Policy } from '@types'

interface Props {
  isOpen: boolean
  policy?: Policy | null
}

interface Emits {
  close: []
  assign: [groupIds: string[]]
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const selectedGroups = ref<string[]>([])
const isAssigning = ref(false)

// Mock device groups for calculations
const mockGroups = [
  { groupId: 'group-1', deviceCount: 8 },
  { groupId: 'group-2', deviceCount: 5 },
  { groupId: 'group-3', deviceCount: 7 },
  { groupId: 'group-4', deviceCount: 12 },
]

const totalAffectedDevices = computed(() => {
  return selectedGroups.value.reduce((sum, groupId) => {
    const group = mockGroups.find(g => g.groupId === groupId)
    return sum + (group?.deviceCount || 0)
  }, 0)
})

const estimatedDuration = computed(() => {
  // Estimate: ~1 second per device for deployment
  return Math.ceil(totalAffectedDevices.value * 1.5)
})

const closeModal = () => {
  if (!isAssigning.value) {
    selectedGroups.value = []
    emit('close')
  }
}

const handleBulkAssign = async () => {
  if (selectedGroups.value.length === 0) return
  
  isAssigning.value = true
  try {
    // Simulate some processing time
    await new Promise(resolve => setTimeout(resolve, 1500))
    
    // Emit the assignment with selected groups
    emit('assign', selectedGroups.value)
    
    // Close modal after success
    setTimeout(() => {
      closeModal()
    }, 500)
  } finally {
    isAssigning.value = false
  }
}
</script>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
