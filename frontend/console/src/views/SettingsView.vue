<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <h1 class="text-3xl font-bold text-gray-900 dark:text-white">System Settings</h1>
      <button
        @click="showResetConfirm = true"
        class="px-4 py-2 text-red-600 dark:text-red-400 border border-red-600 dark:border-red-400 rounded-lg hover:bg-red-50 dark:hover:bg-red-900/20 transition-colors"
      >
        Reset to Defaults
      </button>
    </div>

    <!-- Loading -->
    <div v-if="settingsStore.loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      <p class="mt-4 text-gray-600 dark:text-gray-400">Loading settings...</p>
    </div>

    <!-- Settings Tabs -->
    <div v-else class="space-y-6">
      <!-- Tab Navigation -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow">
        <div class="flex overflow-x-auto border-b border-gray-200 dark:border-gray-700">
          <button
            v-for="category in settingsStore.categoriesWithSettings"
            :key="category.id"
            @click="activeCategory = category.id"
            :class="[
              'px-6 py-4 font-medium whitespace-nowrap transition-colors border-b-2',
              activeCategory === category.id
                ? 'text-blue-600 border-blue-600 dark:text-blue-400 dark:border-blue-400'
                : 'text-gray-600 dark:text-gray-400 border-transparent hover:text-gray-900 dark:hover:text-white'
            ]"
          >
            <span class="mr-2">{{ category.icon }}</span>
            {{ category.name }}
            <span class="ml-2 text-xs bg-gray-200 dark:bg-gray-700 px-2 py-1 rounded-full">
              {{ category.settingCount }}
            </span>
          </button>
        </div>
      </div>

      <!-- Active Category Settings -->
      <div v-if="activeCategoryData" class="space-y-6">
        <!-- Category Description -->
        <div class="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4">
          <p class="text-sm text-blue-700 dark:text-blue-400">
            <strong>{{ activeCategoryData.name }}:</strong> {{ activeCategoryData.description }}
          </p>
        </div>

        <!-- Settings Form -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-6">
          <div v-for="setting in activeCategoryData.settings" :key="setting.id" class="border-b border-gray-200 dark:border-gray-700 pb-6 last:border-0">
            <!-- String Input -->
            <div v-if="setting.type === 'string'" class="space-y-2">
              <div class="flex items-start justify-between">
                <div>
                  <label :for="setting.id" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
                    {{ setting.name }}
                  </label>
                  <p v-if="setting.description" class="text-xs text-gray-600 dark:text-gray-400 mt-1">
                    {{ setting.description }}
                  </p>
                </div>
              </div>
              <input
                :id="setting.id"
                :value="setting.value"
                type="text"
                @change="(e) => updateSetting(setting.id, (e.target as HTMLInputElement).value)"
                :required="setting.required"
                class="w-full px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white placeholder-gray-500 dark:placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <!-- Number Input -->
            <div v-else-if="setting.type === 'number'" class="space-y-2">
              <div class="flex items-start justify-between">
                <div>
                  <label :for="setting.id" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
                    {{ setting.name }}
                  </label>
                  <p v-if="setting.description" class="text-xs text-gray-600 dark:text-gray-400 mt-1">
                    {{ setting.description }}
                  </p>
                </div>
              </div>
              <input
                :id="setting.id"
                :value="setting.value"
                type="number"
                @change="(e) => updateSetting(setting.id, Number((e.target as HTMLInputElement).value))"
                :min="setting.validation?.min"
                :max="setting.validation?.max"
                class="w-full px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              <p v-if="setting.validation" class="text-xs text-gray-600 dark:text-gray-400">
                Range: {{ setting.validation.min }} - {{ setting.validation.max }}
              </p>
            </div>

            <!-- Boolean Toggle -->
            <div v-else-if="setting.type === 'boolean'" class="flex items-center justify-between">
              <div>
                <label :for="setting.id" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
                  {{ setting.name }}
                </label>
                <p v-if="setting.description" class="text-xs text-gray-600 dark:text-gray-400 mt-1">
                  {{ setting.description }}
                </p>
              </div>
              <button
                :id="setting.id"
                @click="updateSetting(setting.id, !setting.value)"
                :class="[
                  'relative inline-flex h-6 w-11 items-center rounded-full transition-colors',
                  setting.value
                    ? 'bg-blue-600'
                    : 'bg-gray-300 dark:bg-gray-600'
                ]"
              >
                <span
                  :class="[
                    'inline-block h-4 w-4 transform rounded-full bg-white transition-transform',
                    setting.value ? 'translate-x-6' : 'translate-x-1'
                  ]"
                ></span>
              </button>
            </div>

            <!-- Select Dropdown -->
            <div v-else-if="setting.type === 'select'" class="space-y-2">
              <div class="flex items-start justify-between">
                <div>
                  <label :for="setting.id" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
                    {{ setting.name }}
                  </label>
                  <p v-if="setting.description" class="text-xs text-gray-600 dark:text-gray-400 mt-1">
                    {{ setting.description }}
                  </p>
                </div>
              </div>
              <select
                :id="setting.id"
                :value="setting.value"
                @change="(e) => updateSetting(setting.id, (e.target as HTMLSelectElement).value)"
                class="w-full px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option v-for="option in setting.options" :key="option.value" :value="option.value">
                  {{ option.label }}
                </option>
              </select>
            </div>
          </div>
        </div>

        <!-- Success Message -->
        <div v-if="showSaveSuccess" class="bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg p-4">
          <p class="text-sm text-green-700 dark:text-green-400">
            ✓ Settings saved successfully
          </p>
        </div>
      </div>
    </div>

    <!-- Reset Confirmation Modal -->
    <div v-if="showResetConfirm" class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg p-6 max-w-sm">
        <h3 class="text-lg font-semibold text-gray-900 dark:text-white mb-4">Reset to Default Settings?</h3>
        <p class="text-gray-600 dark:text-gray-400 mb-6">
          This will reset all settings to their default values. All custom configuration will be lost. This action cannot be undone.
        </p>
        <div class="flex gap-3 justify-end">
          <button
            @click="showResetConfirm = false"
            class="px-4 py-2 border border-gray-300 dark:border-gray-600 rounded-lg text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700"
          >
            Cancel
          </button>
          <button
            @click="performReset"
            class="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
          >
            Reset All Settings
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'

const settingsStore = useSettingsStore()

// State
const activeCategory = ref('general')
const showResetConfirm = ref(false)
const showSaveSuccess = ref(false)

// Computed
const activeCategoryData = computed(() => {
  return settingsStore.categoriesWithSettings.find(cat => cat.id === activeCategory.value)
})

// Methods
onMounted(() => {
  settingsStore.fetchSettings()
})

const updateSetting = async (settingId: string, value: string | number | boolean) => {
  await settingsStore.updateSetting(settingId, value)
  showSaveSuccess.value = true
  setTimeout(() => {
    showSaveSuccess.value = false
  }, 3000)
}

const performReset = async () => {
  await settingsStore.resetSettings()
  showResetConfirm.value = false
  showSaveSuccess.value = true
  setTimeout(() => {
    showSaveSuccess.value = false
  }, 3000)
}
</script>
