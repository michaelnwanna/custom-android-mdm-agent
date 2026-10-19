<template>
  <div class="space-y-4">
    <!-- Header -->
    <div class="flex items-center justify-between mb-4">
      <h3 class="text-lg font-semibold text-gray-900 dark:text-white">Select Device Groups</h3>
      <button
        @click="toggleSelectAll"
        :class="[
          'text-sm font-medium px-3 py-1 rounded-lg transition',
          allSelected
            ? 'bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300'
            : 'bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300'
        ]"
      >
        {{ allSelected ? 'Deselect All' : 'Select All' }}
      </button>
    </div>

    <!-- Groups Grid -->
    <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
      <div
        v-for="group in deviceGroups"
        :key="group.groupId"
        :class="[
          'p-4 rounded-lg border-2 cursor-pointer transition',
          isGroupSelected(group.groupId)
            ? 'border-blue-600 dark:border-blue-500 bg-blue-50 dark:bg-blue-900/20'
            : 'border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 hover:border-gray-300 dark:hover:border-gray-600'
        ]"
        @click="toggleGroup(group.groupId)"
      >
        <!-- Checkbox + Title -->
        <div class="flex items-start gap-3 mb-2">
          <div class="mt-1">
            <input
              type="checkbox"
              :checked="isGroupSelected(group.groupId)"
              class="w-5 h-5 text-blue-600 dark:text-blue-500 rounded focus:ring-2 focus:ring-blue-500 cursor-pointer"
              :readonly="true"
            />
          </div>
          <div class="flex-1 min-w-0">
            <h4 class="font-semibold text-gray-900 dark:text-white truncate">{{ group.name }}</h4>
            <p class="text-sm text-gray-600 dark:text-gray-400 truncate">{{ group.description }}</p>
          </div>
        </div>

        <!-- Stats -->
        <div class="flex gap-4 text-sm ml-8">
          <div>
            <span class="text-gray-600 dark:text-gray-400">Devices:</span>
            <span class="font-medium text-gray-900 dark:text-white ml-1">{{ group.deviceCount }}</span>
          </div>
          <div>
            <span class="text-gray-600 dark:text-gray-400">Compliance:</span>
            <span :class="['font-medium ml-1', getComplianceColorClass(group.complianceScore)]">
              {{ group.complianceScore }}%
            </span>
          </div>
        </div>

        <!-- Progress Bar -->
        <div class="mt-2 ml-8">
          <div class="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-1.5 overflow-hidden">
            <div
              class="h-1.5 rounded-full transition-all"
              :class="getComplianceProgressClass(group.complianceScore)"
              :style="{ width: group.complianceScore + '%' }"
            ></div>
          </div>
        </div>
      </div>
    </div>

    <!-- Summary -->
    <div v-if="selectedGroups.length > 0" class="bg-blue-50 dark:bg-blue-900/20 rounded-lg p-4 border border-blue-200 dark:border-blue-800">
      <div class="flex items-center justify-between">
        <div>
          <p class="font-medium text-gray-900 dark:text-white">
            {{ selectedGroups.length }} group{{ selectedGroups.length !== 1 ? 's' : '' }} selected
          </p>
          <p class="text-sm text-gray-600 dark:text-gray-400">
            Total: {{ totalSelectedDevices }} device{{ totalSelectedDevices !== 1 ? 's' : '' }}
          </p>
        </div>
        <div class="text-right">
          <div class="text-2xl font-bold text-blue-600 dark:text-blue-400">
            {{ selectedGroups.length }}
          </div>
          <div class="text-xs text-gray-600 dark:text-gray-400">groups</div>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <div v-else class="text-center py-8 text-gray-600 dark:text-gray-400">
      <svg class="w-12 h-12 mx-auto mb-2 opacity-50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4" />
      </svg>
      <p class="text-sm">No device groups selected</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import type { DeviceGroup } from '@types'

interface Props {
  modelValue?: string[]
  availableGroups?: DeviceGroup[]
}

interface Emits {
  'update:modelValue': [value: string[]]
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: () => [],
  availableGroups: () => []
})

const emit = defineEmits<Emits>()

const selectedGroups = computed({
  get: () => props.modelValue || [],
  set: (value) => emit('update:modelValue', value)
})

// Mock device groups if none provided
const deviceGroups = computed(() => {
  if (props.availableGroups && props.availableGroups.length > 0) {
    return props.availableGroups
  }
  
  return [
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
    {
      groupId: 'group-4',
      name: 'Staging Devices',
      description: 'Pre-production staging environment',
      deviceCount: 12,
      complianceScore: 92,
    },
  ]
})

const totalSelectedDevices = computed(() => {
  return deviceGroups.value
    .filter(g => selectedGroups.value.includes(g.groupId))
    .reduce((sum, g) => sum + g.deviceCount, 0)
})

const allSelected = computed(() => {
  return selectedGroups.value.length === deviceGroups.value.length
})

const isGroupSelected = (groupId: string): boolean => {
  return selectedGroups.value.includes(groupId)
}

const toggleGroup = (groupId: string) => {
  const index = selectedGroups.value.indexOf(groupId)
  if (index > -1) {
    selectedGroups.value = selectedGroups.value.filter(id => id !== groupId)
  } else {
    selectedGroups.value = [...selectedGroups.value, groupId]
  }
}

const toggleSelectAll = () => {
  if (allSelected.value) {
    selectedGroups.value = []
  } else {
    selectedGroups.value = deviceGroups.value.map(g => g.groupId)
  }
}

const getComplianceColorClass = (compliance: number): string => {
  if (compliance >= 90) return 'text-green-600 dark:text-green-400'
  if (compliance >= 75) return 'text-blue-600 dark:text-blue-400'
  if (compliance >= 50) return 'text-amber-600 dark:text-amber-400'
  return 'text-red-600 dark:text-red-400'
}

const getComplianceProgressClass = (compliance: number): string => {
  if (compliance >= 90) return 'bg-green-600 dark:bg-green-500'
  if (compliance >= 75) return 'bg-blue-600 dark:bg-blue-500'
  if (compliance >= 50) return 'bg-amber-600 dark:bg-amber-500'
  return 'bg-red-600 dark:bg-red-500'
}
</script>
