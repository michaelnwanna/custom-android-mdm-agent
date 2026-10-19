<template>
  <transition name="fade">
    <div
      v-if="isOpen"
      class="fixed inset-0 z-40 flex items-center justify-center bg-black/50 p-4"
      @click.self="close"
    >
      <section
        class="flex max-h-[90vh] w-full max-w-2xl flex-col overflow-hidden rounded-lg bg-white shadow-xl dark:bg-gray-800"
        role="dialog"
        aria-modal="true"
        aria-labelledby="policy-assignment-title"
      >
        <header class="flex items-center justify-between bg-blue-600 px-6 py-4 text-white">
          <div>
            <h2 id="policy-assignment-title" class="text-xl font-bold">Assign Policy</h2>
            <p class="mt-1 text-sm text-blue-100">{{ policy?.name }}</p>
          </div>
          <button
            type="button"
            aria-label="Close"
            :disabled="isAssigning"
            class="rounded p-1 transition hover:bg-white/20 disabled:opacity-50"
            @click="close"
          >
            <svg class="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </header>

        <div class="flex-1 space-y-4 overflow-y-auto p-6">
          <p class="text-sm text-gray-600 dark:text-gray-300">
            Select active enrolled devices. Assignments are queued by the server; delivery status updates when a device acknowledges the policy.
          </p>

          <div v-if="loadError" role="alert" class="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-900/20 dark:text-red-200">
            {{ loadError }}
            <button type="button" class="ml-2 font-semibold underline" @click="loadDevices">Retry</button>
          </div>

          <div v-if="isLoading" class="py-10 text-center text-gray-600 dark:text-gray-300">
            Loading enrolled devices...
          </div>

          <template v-else-if="!loadError">
            <input
              v-model="searchQuery"
              type="search"
              placeholder="Search devices..."
              aria-label="Search enrolled devices"
              class="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-gray-900 dark:border-gray-600 dark:bg-gray-700 dark:text-white"
            />

            <div class="flex items-center justify-between text-sm text-gray-600 dark:text-gray-300">
              <span>{{ selectedDevices.length }} selected · {{ filteredDevices.length }} shown</span>
              <button type="button" class="font-medium text-blue-600 hover:underline dark:text-blue-400" @click="toggleAll">
                {{ allVisibleSelected ? 'Deselect shown' : 'Select shown' }}
              </button>
            </div>

            <div class="max-h-64 space-y-1 overflow-y-auto rounded-lg border border-gray-200 dark:border-gray-700">
              <p v-if="filteredDevices.length === 0" class="p-6 text-center text-sm text-gray-500">
                {{ devices.length === 0 ? 'No active enrolled devices are available.' : 'No devices match your search.' }}
              </p>
              <label
                v-for="device in filteredDevices"
                :key="device.enrollmentId"
                class="flex cursor-pointer items-center gap-3 p-3 hover:bg-gray-50 dark:hover:bg-gray-700/50"
              >
                <input
                  v-model="selectedDevices"
                  type="checkbox"
                  :value="device.enrollmentId"
                  :disabled="isAssigning"
                  class="rounded text-blue-600"
                />
                <span class="min-w-0 flex-1">
                  <span class="block truncate text-sm font-medium text-gray-900 dark:text-white">{{ device.deviceName }}</span>
                  <span class="block truncate text-xs text-gray-500 dark:text-gray-400">
                    {{ device.deviceId }} · {{ device.deviceState }}
                  </span>
                </span>
              </label>
            </div>
          </template>

          <section v-if="isAssigning || assignmentStatuses.length > 0" aria-live="polite" class="space-y-3 rounded-lg border border-blue-200 bg-blue-50 p-4 dark:border-blue-800 dark:bg-blue-900/20">
            <div class="flex justify-between text-sm">
              <span class="font-medium text-gray-800 dark:text-gray-100">
                {{ isAssigning ? 'Submitting assignment requests...' : 'Assignment results' }}
              </span>
              <span class="text-gray-600 dark:text-gray-300">{{ assignmentProgress }}%</span>
            </div>
            <div class="h-2 overflow-hidden rounded-full bg-gray-200 dark:bg-gray-700">
              <div class="h-full bg-blue-600 transition-all" :style="{ width: `${assignmentProgress}%` }"></div>
            </div>
            <ul class="max-h-40 space-y-1 overflow-y-auto text-sm">
              <li
                v-for="result in assignmentStatuses"
                :key="result.enrollmentId"
                class="flex justify-between gap-3"
                :class="result.status === 'queued' ? 'text-green-700 dark:text-green-300' : 'text-red-700 dark:text-red-300'"
              >
                <span class="truncate">{{ deviceName(result.enrollmentId) }}</span>
                <span class="shrink-0">{{ result.message }}</span>
              </li>
            </ul>
          </section>
        </div>

        <footer class="flex justify-end gap-3 border-t border-gray-200 bg-gray-50 px-6 py-4 dark:border-gray-700 dark:bg-gray-700/50">
          <button
            type="button"
            :disabled="isAssigning"
            class="rounded-lg border border-gray-300 px-4 py-2 font-medium text-gray-700 hover:bg-gray-100 disabled:opacity-50 dark:border-gray-600 dark:text-gray-200 dark:hover:bg-gray-700"
            @click="close"
          >
            {{ assignmentStatuses.length ? 'Close' : 'Cancel' }}
          </button>
          <button
            v-if="assignmentStatuses.length === 0"
            type="button"
            :disabled="isAssigning || isLoading || loadError !== null || selectedDevices.length === 0"
            class="rounded-lg bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-gray-400"
            @click="$emit('assign', [...selectedDevices])"
          >
            {{ isAssigning ? 'Assigning...' : `Assign to ${selectedDevices.length} device(s)` }}
          </button>
        </footer>
      </section>
    </div>
  </transition>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  fetchAssignableDevices,
  type AssignmentStatus,
  type PolicyManagementDevice
} from '@/api/policyApi'
import type { Policy } from '@types'

interface Props {
  isOpen: boolean
  policy: Policy | null
  isAssigning: boolean
  assignmentProgress: number
  assignmentStatuses: AssignmentStatus[]
}

const props = defineProps<Props>()
const emit = defineEmits<{
  close: []
  assign: [enrollmentIds: string[]]
}>()

const devices = ref<PolicyManagementDevice[]>([])
const selectedDevices = ref<string[]>([])
const searchQuery = ref('')
const isLoading = ref(false)
const loadError = ref<string | null>(null)

const filteredDevices = computed(() => {
  const query = searchQuery.value.trim().toLowerCase()
  if (!query) return devices.value
  return devices.value.filter(device =>
    device.deviceName.toLowerCase().includes(query) ||
    device.deviceId.toLowerCase().includes(query)
  )
})

const allVisibleSelected = computed(() =>
  filteredDevices.value.length > 0 &&
  filteredDevices.value.every(device => selectedDevices.value.includes(device.enrollmentId))
)

const loadDevices = async () => {
  isLoading.value = true
  loadError.value = null
  try {
    devices.value = await fetchAssignableDevices()
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : 'Unable to load enrolled devices.'
  } finally {
    isLoading.value = false
  }
}

const toggleAll = () => {
  const visibleIds = filteredDevices.value.map(device => device.enrollmentId)
  selectedDevices.value = allVisibleSelected.value
    ? selectedDevices.value.filter(id => !visibleIds.includes(id))
    : [...new Set([...selectedDevices.value, ...visibleIds])]
}

const deviceName = (enrollmentId: string) =>
  devices.value.find(device => device.enrollmentId === enrollmentId)?.deviceName ?? enrollmentId

const close = () => {
  if (!props.isAssigning) emit('close')
}

watch(
  () => props.isOpen,
  open => {
    if (open) {
      devices.value = []
      selectedDevices.value = []
      searchQuery.value = ''
      void loadDevices()
    }
  }
)
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
