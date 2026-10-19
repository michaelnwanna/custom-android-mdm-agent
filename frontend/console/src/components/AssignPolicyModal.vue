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
        class="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-2xl w-full max-h-96 overflow-hidden flex flex-col"
        @click.stop
      >
        <!-- Header -->
        <div class="bg-gradient-to-r from-blue-500 to-blue-600 dark:from-blue-700 dark:to-blue-800 px-6 py-4 flex items-center justify-between">
          <h2 class="text-xl font-bold text-white">Assign Policy</h2>
          <button
            @click="closeModal"
            class="text-white hover:bg-white hover:bg-opacity-20 p-1 rounded transition"
          >
            <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-6 space-y-6">
          <!-- Policy Info -->
          <div v-if="policy" class="bg-blue-50 dark:bg-blue-900/20 p-4 rounded-lg border border-blue-200 dark:border-blue-800">
            <h3 class="font-semibold text-gray-900 dark:text-white mb-1">{{ policy.name }}</h3>
            <p class="text-sm text-gray-600 dark:text-gray-400">{{ policy.description }}</p>
            <div class="flex gap-4 mt-2 text-xs">
              <span class="text-gray-600 dark:text-gray-400"><strong>Type:</strong> {{ policy.type }}</span>
              <span class="text-gray-600 dark:text-gray-400"><strong>Version:</strong> v{{ policy.version || 1 }}</span>
            </div>
          </div>

          <!-- Device Selection Tabs -->
          <div class="border-b border-gray-200 dark:border-gray-700">
            <div class="flex gap-4">
              <button
                @click="selectionMode = 'individual'"
                :class="[
                  'px-4 py-2 font-medium border-b-2 transition',
                  selectionMode === 'individual'
                    ? 'border-blue-600 text-blue-600 dark:text-blue-400'
                    : 'border-transparent text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white'
                ]"
              >
                Individual Devices
              </button>
              <button
                @click="selectionMode = 'group'"
                :class="[
                  'px-4 py-2 font-medium border-b-2 transition',
                  selectionMode === 'group'
                    ? 'border-blue-600 text-blue-600 dark:text-blue-400'
                    : 'border-transparent text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white'
                ]"
              >
                Device Groups
              </button>
            </div>
          </div>

          <!-- Individual Device Selection -->
          <div v-if="selectionMode === 'individual'" class="space-y-4">
            <!-- Search -->
            <div>
              <input
                v-model="searchQuery"
                type="text"
                placeholder="Search devices..."
                class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              />
            </div>

            <!-- Device List -->
            <div class="space-y-2 max-h-48 overflow-y-auto">
              <div v-if="filteredDevices.length === 0" class="text-center py-8 text-gray-500 dark:text-gray-400">
                No devices found
              </div>
              <label v-for="device in filteredDevices" :key="device.deviceId" class="flex items-center gap-3 p-2 hover:bg-gray-50 dark:hover:bg-gray-700/50 rounded cursor-pointer">
                <input
                  type="checkbox"
                  :checked="selectedDevices.includes(device.deviceId)"
                  @change="toggleDevice(device.deviceId)"
                  class="rounded"
                />
                <div class="flex-1 min-w-0">
                  <p class="font-medium text-gray-900 dark:text-white text-sm">{{ device.name }}</p>
                  <p class="text-xs text-gray-600 dark:text-gray-400">{{ device.deviceId }} · {{ device.status }}</p>
                </div>
                <span class="text-xs px-2 py-1 bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400 rounded">
                  {{ device.status }}
                </span>
              </label>
            </div>

            <!-- Select All -->
            <div class="border-t border-gray-200 dark:border-gray-700 pt-2">
              <label class="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  :checked="selectedDevices.length === filteredDevices.length && filteredDevices.length > 0"
                  @change="toggleSelectAll"
                  class="rounded"
                />
                <span class="text-sm font-medium text-gray-700 dark:text-gray-300">
                  Select all {{ filteredDevices.length }} device(s)
                </span>
              </label>
            </div>
          </div>

          <!-- Group Selection -->
          <div v-else-if="selectionMode === 'group'" class="space-y-4">
            <div class="space-y-2">
              <label v-for="group in deviceGroups" :key="group.groupId" class="flex items-center gap-3 p-3 border border-gray-200 dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-700/50 rounded-lg cursor-pointer">
                <input
                  type="radio"
                  :value="group.groupId"
                  v-model="selectedGroup"
                  class="rounded-full"
                />
                <div class="flex-1">
                  <p class="font-medium text-gray-900 dark:text-white">{{ group.name }}</p>
                  <p class="text-xs text-gray-600 dark:text-gray-400">{{ group.deviceCount }} devices</p>
                </div>
              </label>
            </div>
          </div>

          <!-- Selected Count -->
          <div class="bg-gradient-to-r from-blue-50 to-blue-100 dark:from-blue-900/30 dark:to-blue-800/30 p-4 rounded-lg border border-blue-200 dark:border-blue-800">
            <p class="text-sm text-gray-700 dark:text-gray-300">
              <strong>Deployment Scope:</strong>
              <span v-if="selectionMode === 'individual'" class="font-semibold text-blue-600 dark:text-blue-400">
                {{ selectedDevices.length }} device(s) selected
              </span>
              <span v-else class="font-semibold text-blue-600 dark:text-blue-400">
                {{ selectedGroupDeviceCount }} device(s) in selected group
              </span>
            </p>
          </div>
        </div>

        <!-- Footer -->
        <div class="bg-gray-50 dark:bg-gray-700/50 px-6 py-4 border-t border-gray-200 dark:border-gray-700 flex gap-3 justify-end">
          <button
            @click="closeModal"
            class="px-4 py-2 border border-gray-300 dark:border-gray-600 text-gray-700 dark:text-gray-300 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-700 transition font-medium"
          >
            Cancel
          </button>
          <button
            @click="handleAssign"
            :disabled="isAssigning || (selectionMode === 'individual' && selectedDevices.length === 0) || (selectionMode === 'group' && !selectedGroup)"
            class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition font-medium flex items-center gap-2"
          >
            <svg v-if="isAssigning" class="w-4 h-4 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
            {{ isAssigning ? 'Assigning...' : 'Assign Policy' }}
          </button>
        </div>
      </div>
    </div>
  </transition>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import type { Policy, Device, DeviceGroup } from '@types'

interface Props {
  isOpen: boolean
  policy?: Policy | null
}

interface Emits {
  close: []
  assign: [deviceIds: string[], groupId?: string]
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const selectedDevices = ref<string[]>([])
const selectedGroup = ref<string>('')
const searchQuery = ref('')
const selectionMode = ref<'individual' | 'group'>('individual')
const isAssigning = ref(false)

// Mock device data
const mockDevices: Device[] = Array.from({ length: 20 }, (_, i) => ({
  deviceId: `dev-${(i + 1).toString().padStart(3, '0')}`,
  name: `Device ${i + 1}`,
  status: i % 3 === 0 ? 'Offline' : 'Online',
  enrolledDate: new Date(Date.now() - Math.random() * 90 * 24 * 60 * 60 * 1000).toISOString(),
})) as Device[]

// Mock device groups
const deviceGroups: DeviceGroup[] = [
  {
    groupId: 'group-1',
    name: 'Production Devices',
    description: 'All production environment devices',
    deviceCount: 8,
    complianceScore: 95,
  },
  {
    groupId: 'group-2',
    name: 'Testing Devices',
    description: 'QA and testing environment devices',
    deviceCount: 5,
    complianceScore: 88,
  },
  {
    groupId: 'group-3',
    name: 'Development Devices',
    description: 'Developer personal devices',
    deviceCount: 7,
    complianceScore: 75,
  },
]

const filteredDevices = computed(() => {
  if (!searchQuery.value) return mockDevices
  const query = searchQuery.value.toLowerCase()
  return mockDevices.filter(d =>
    d.name.toLowerCase().includes(query) ||
    d.deviceId.toLowerCase().includes(query)
  )
})

const selectedGroupDeviceCount = computed(() => {
  const group = deviceGroups.find(g => g.groupId === selectedGroup.value)
  return group?.deviceCount || 0
})

const toggleDevice = (deviceId: string) => {
  const index = selectedDevices.value.indexOf(deviceId)
  if (index > -1) {
    selectedDevices.value.splice(index, 1)
  } else {
    selectedDevices.value.push(deviceId)
  }
}

const toggleSelectAll = () => {
  if (selectedDevices.value.length === filteredDevices.value.length) {
    selectedDevices.value = []
  } else {
    selectedDevices.value = filteredDevices.value.map(d => d.deviceId)
  }
}

const closeModal = () => {
  selectedDevices.value = []
  selectedGroup.value = ''
  searchQuery.value = ''
  emit('close')
}

const handleAssign = async () => {
  isAssigning.value = true
  try {
    if (selectionMode.value === 'individual') {
      emit('assign', selectedDevices.value)
    } else {
      emit('assign', [], selectedGroup.value)
    }
    closeModal()
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
