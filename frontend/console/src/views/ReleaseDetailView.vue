<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-3">
        <router-link
          to="/releases"
          class="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300"
        >
          ← Back to Releases
        </router-link>
        <span class="text-gray-400">/</span>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">
          {{ isNew ? 'Create Release' : 'Release Details' }}
        </h1>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading release...</p>
    </div>

    <!-- Not Found State -->
    <div v-else-if="!isNew && !release" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">Release not found</h3>
      <p class="text-gray-600 dark:text-gray-400">The release you're looking for doesn't exist.</p>
    </div>

    <!-- Release Form -->
    <div v-else-if="release" class="space-y-6">
      <!-- Status & Priority -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
        <!-- Status -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-4">Status</h3>
          <span :class="[
            'px-4 py-2 rounded-full text-sm font-bold inline-block',
            release.status === 'Published' ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200' :
            release.status === 'Draft' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
            'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
          ]">
            {{ release.status }}
          </span>
        </div>

        <!-- Priority -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <label class="block text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-4">Priority</label>
          <select
            v-model="editForm.priority"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="Critical">Critical</option>
            <option value="High">High</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>
        </div>

        <!-- Deployment Status -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-4">Deployment</h3>
          <span :class="[
            'px-4 py-2 rounded-full text-sm font-bold inline-block',
            release.deploymentStatus === 'Completed' ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200' :
            release.deploymentStatus === 'In Progress' ? 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200' :
            release.deploymentStatus === 'Failed' ? 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200' :
            'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
          ]">
            {{ release.deploymentStatus }}
          </span>
        </div>
      </div>

      <!-- Release Information -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
        <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Release Information</h3>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <!-- Package Name -->
          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Package Name</label>
            <input
              v-model="editForm.packageName"
              type="text"
              placeholder="com.example.app"
              class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent font-mono text-sm"
            />
          </div>

          <!-- Version Name -->
          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Version Name</label>
            <input
              v-model="editForm.versionName"
              type="text"
              placeholder="1.0.0"
              class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            />
          </div>

          <!-- Version Code -->
          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Version Code</label>
            <input
              v-model.number="editForm.versionCode"
              type="number"
              class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            />
          </div>

          <!-- File Size (MB) -->
          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">File Size (MB)</label>
            <input
              v-model.number="editForm.fileSize"
              type="number"
              class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            />
          </div>
        </div>

        <!-- Download URL -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Download URL</label>
          <input
            v-model="editForm.downloadUrl"
            type="url"
            placeholder="https://..."
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent font-mono text-sm"
          />
        </div>

        <!-- Release ID (read-only) -->
        <div v-if="!isNew">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Release ID</label>
          <input
            :value="release.releaseId"
            type="text"
            disabled
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400 font-mono text-sm cursor-not-allowed"
          />
        </div>
      </div>

      <!-- Changelog & Android Compatibility -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <!-- Changelog -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Changelog</label>
          <textarea
            v-model="editForm.changelog"
            rows="6"
            placeholder="Describe changes in this release..."
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Android Compatibility -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide">Android Compatibility</h3>
          
          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Minimum Version</label>
            <select
              v-model="editForm.minAndroidVersion"
              class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            >
              <option value="11">Android 11</option>
              <option value="12">Android 12</option>
              <option value="13">Android 13</option>
              <option value="14">Android 14</option>
            </select>
          </div>

          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Maximum Version</label>
            <input
              v-model="editForm.maxAndroidVersion"
              type="text"
              placeholder="14"
              class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            />
          </div>

          <div class="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-3">
            <p class="text-sm text-blue-800 dark:text-blue-200">
              Supports Android {{ editForm.minAndroidVersion }} to {{ editForm.maxAndroidVersion }}
            </p>
          </div>
        </div>
      </div>

      <!-- Deployment Metrics -->
      <div class="grid grid-cols-2 md:grid-cols-4 gap-6">
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 text-center">
          <p class="text-sm text-gray-600 dark:text-gray-400 mb-2">Target Devices</p>
          <p class="text-3xl font-bold text-gray-900 dark:text-white">{{ release.targetDeviceCount }}</p>
        </div>
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 text-center">
          <p class="text-sm text-gray-600 dark:text-gray-400 mb-2">Installed</p>
          <p class="text-3xl font-bold text-gray-900 dark:text-white">{{ release.installedDeviceCount }}</p>
        </div>
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 text-center">
          <p class="text-sm text-gray-600 dark:text-gray-400 mb-2">Downloads</p>
          <p class="text-3xl font-bold text-gray-900 dark:text-white">{{ release.downloadCount }}</p>
        </div>
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 text-center">
          <p class="text-sm text-gray-600 dark:text-gray-400 mb-2">Errors</p>
          <p class="text-3xl font-bold" :class="release.errorCount > 0 ? 'text-red-600 dark:text-red-400' : 'text-gray-900 dark:text-white'">
            {{ release.errorCount }}
          </p>
        </div>
      </div>

      <!-- Deployment Groups -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Deployment Groups</h3>
        <div class="space-y-2">
          <div class="flex items-center">
            <input
              type="checkbox"
              id="group-standard"
              v-model="editForm.deploymentGroups"
              value="Standard"
              class="rounded"
            />
            <label for="group-standard" class="ml-2 text-gray-700 dark:text-gray-300">Standard</label>
          </div>
          <div class="flex items-center">
            <input
              type="checkbox"
              id="group-beta"
              v-model="editForm.deploymentGroups"
              value="Beta Testers"
              class="rounded"
            />
            <label for="group-beta" class="ml-2 text-gray-700 dark:text-gray-300">Beta Testers</label>
          </div>
          <div class="flex items-center">
            <input
              type="checkbox"
              id="group-enterprise"
              v-model="editForm.deploymentGroups"
              value="Enterprise"
              class="rounded"
            />
            <label for="group-enterprise" class="ml-2 text-gray-700 dark:text-gray-300">Enterprise</label>
          </div>
          <div class="flex items-center">
            <input
              type="checkbox"
              id="group-dev"
              v-model="editForm.deploymentGroups"
              value="Development"
              class="rounded"
            />
            <label for="group-dev" class="ml-2 text-gray-700 dark:text-gray-300">Development</label>
          </div>
        </div>
      </div>

      <!-- Timestamps -->
      <div v-if="!isNew" class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-2">Released</h3>
          <p class="text-gray-900 dark:text-white">{{ formatDateTime(release.releaseDate) }}</p>
          <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">{{ formatTimeAgo(release.releaseDate) }}</p>
        </div>
      </div>

      <!-- Action Buttons -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 flex gap-3 justify-end">
        <router-link
          to="/releases"
          class="px-4 py-2 bg-gray-300 dark:bg-gray-600 text-gray-900 dark:text-white rounded-lg hover:bg-gray-400 dark:hover:bg-gray-500 transition"
        >
          Cancel
        </router-link>
        <button
          @click="saveReleaseChanges"
          :disabled="saving"
          class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 transition"
        >
          {{ saving ? 'Saving...' : isNew ? 'Create Release' : 'Save Changes' }}
        </button>
        <button
          v-if="!isNew && release.status === 'Draft'"
          @click="publishRelease"
          :disabled="saving"
          class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M9 19l3 3m0 0l3-3m-3 3v-6" />
          </svg>
          {{ saving ? 'Publishing...' : 'Publish' }}
        </button>
        <button
          v-else-if="!isNew && release.status === 'Published'"
          @click="deprecateRelease"
          :disabled="saving"
          class="px-4 py-2 bg-yellow-600 text-white rounded-lg hover:bg-yellow-700 disabled:bg-gray-400 transition"
        >
          {{ saving ? 'Deprecating...' : 'Deprecate' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, reactive, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useReleaseStore, type ReleaseDetails } from '@stores/releaseStore'

const route = useRoute()
const router = useRouter()
const releaseStore = useReleaseStore()

const loading = ref(false)
const saving = ref(false)

const releaseId = computed(() => route.params.id as string)
const isNew = computed(() => releaseId.value === 'new')

const release = computed(() => {
  if (isNew.value) {
    return {
      releaseId: 'new',
      packageName: '',
      versionCode: 1,
      versionName: '1.0.0',
      releaseDate: new Date().toISOString(),
      fileSize: 50,
      downloadUrl: '',
      status: 'Draft',
      changelog: '',
      deploymentGroups: [],
      downloadCount: 0,
      installationCount: 0,
      errorCount: 0,
      deploymentStatus: 'Pending' as const,
      priority: 'Medium' as const,
      minAndroidVersion: '11',
      maxAndroidVersion: '14',
      targetDeviceCount: 0,
      installedDeviceCount: 0
    } as ReleaseDetails
  }
  return releaseStore.getReleaseById(releaseId.value)
})

const editForm = reactive<Partial<ReleaseDetails>>({
  packageName: '',
  versionCode: 1,
  versionName: '1.0.0',
  fileSize: 50,
  downloadUrl: '',
  changelog: '',
  deploymentGroups: [],
  priority: 'Medium',
  minAndroidVersion: '11',
  maxAndroidVersion: '14'
})

function initializeForm() {
  if (!isNew.value && release.value) {
    editForm.packageName = release.value.packageName
    editForm.versionCode = release.value.versionCode
    editForm.versionName = release.value.versionName
    editForm.fileSize = release.value.fileSize
    editForm.downloadUrl = release.value.downloadUrl
    editForm.changelog = release.value.changelog
    editForm.deploymentGroups = [...(release.value.deploymentGroups || [])]
    editForm.priority = release.value.priority
    editForm.minAndroidVersion = release.value.minAndroidVersion
    editForm.maxAndroidVersion = release.value.maxAndroidVersion
  }
}

async function saveReleaseChanges() {
  if (!editForm.packageName || !editForm.versionName) {
    return
  }

  saving.value = true
  try {
    if (isNew.value) {
      await releaseStore.createRelease({
        packageName: editForm.packageName,
        versionCode: editForm.versionCode || 1,
        versionName: editForm.versionName,
        fileSize: editForm.fileSize || 50,
        downloadUrl: editForm.downloadUrl || '',
        status: 'Draft',
        changelog: editForm.changelog || '',
        deploymentGroups: editForm.deploymentGroups || [],
        downloadCount: 0,
        installationCount: 0,
        errorCount: 0,
        deploymentStatus: 'Pending',
        priority: editForm.priority || 'Medium',
        minAndroidVersion: editForm.minAndroidVersion || '11',
        maxAndroidVersion: editForm.maxAndroidVersion || '14',
        targetDeviceCount: 0,
        installedDeviceCount: 0
      })
    } else if (release.value) {
      await releaseStore.updateRelease(release.value.releaseId, {
        packageName: editForm.packageName,
        versionCode: editForm.versionCode,
        versionName: editForm.versionName,
        fileSize: editForm.fileSize,
        downloadUrl: editForm.downloadUrl,
        changelog: editForm.changelog,
        deploymentGroups: editForm.deploymentGroups,
        priority: editForm.priority,
        minAndroidVersion: editForm.minAndroidVersion,
        maxAndroidVersion: editForm.maxAndroidVersion
      })
    }
    router.push('/releases')
  } finally {
    saving.value = false
  }
}

async function publishRelease() {
  if (!release.value) return
  saving.value = true
  try {
    await releaseStore.publishRelease(release.value.releaseId)
  } finally {
    saving.value = false
  }
}

async function deprecateRelease() {
  if (!release.value) return
  saving.value = true
  try {
    await releaseStore.deprecateRelease(release.value.releaseId)
  } finally {
    saving.value = false
  }
}

function formatDateTime(timestamp: string): string {
  return new Date(timestamp).toLocaleString()
}

function formatTimeAgo(timestamp: string): string {
  const date = new Date(timestamp)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffDays = Math.floor(diffMs / 86400000)

  if (diffDays === 0) return 'Today'
  if (diffDays === 1) return 'Yesterday'
  if (diffDays < 30) return `${diffDays} days ago`
  return `${Math.floor(diffDays / 30)} months ago`
}

onMounted(() => {
  if (!isNew.value && !releaseStore.releases.length) {
    loading.value = true
    releaseStore.fetchReleases().then(() => {
      loading.value = false
      initializeForm()
    })
  } else if (!isNew.value && release.value) {
    initializeForm()
  }
})
</script>
