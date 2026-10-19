<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-3">
        <router-link
          to="/policies"
          class="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300"
        >
          ← Back to Policies
        </router-link>
        <span class="text-gray-400">/</span>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white">
          {{ isNew ? 'Create Policy' : 'Policy Details' }}
        </h1>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <div class="flex justify-center mb-4">
        <div class="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin"></div>
      </div>
      <p class="text-gray-600 dark:text-gray-400">Loading policy...</p>
    </div>

    <!-- Not Found State -->
    <div v-else-if="!isNew && !policy" class="bg-white dark:bg-gray-800 rounded-lg shadow p-12 text-center">
      <svg class="w-16 h-16 text-gray-300 dark:text-gray-600 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <h3 class="text-lg font-medium text-gray-900 dark:text-white mb-2">Policy not found</h3>
      <p class="text-gray-600 dark:text-gray-400">The policy you're looking for doesn't exist.</p>
    </div>

    <!-- Policy Form -->
    <div v-else class="space-y-6">
      <!-- Status & Enforcement -->
      <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
        <!-- Status -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-4">Status</h3>
          <span :class="[
            'px-4 py-2 rounded-full text-sm font-bold inline-block',
            policy.status === 'Active' ? 'bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200' :
            policy.status === 'Draft' ? 'bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200' :
            'bg-gray-100 dark:bg-gray-700 text-gray-800 dark:text-gray-300'
          ]">
            {{ policy.status }}
          </span>
        </div>

        <!-- Enforcement Level -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <label class="block text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-4">Enforcement Level</label>
          <select
            v-model="editForm.enforcementLevel"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          >
            <option value="Strict">Strict</option>
            <option value="Standard">Standard</option>
            <option value="Permissive">Permissive</option>
          </select>
        </div>

        <!-- Device Count -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-4">Devices Affected</h3>
          <p class="text-3xl font-bold text-gray-900 dark:text-white">{{ policy.deviceCount }}</p>
          <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">devices will receive this policy</p>
        </div>
      </div>

      <!-- Policy Information -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 space-y-4">
        <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Policy Information</h3>

        <!-- Policy Name -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Policy Name</label>
          <input
            v-model="editForm.name"
            type="text"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Description -->
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Description</label>
          <textarea
            v-model="editForm.description"
            rows="4"
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>

        <!-- Policy ID (read-only) -->
        <div v-if="!isNew">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">Policy ID</label>
          <input
            :value="policy.policyId"
            type="text"
            disabled
            class="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400 font-mono text-sm cursor-not-allowed"
          />
        </div>
      </div>

      <!-- Rules -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <div class="flex items-center justify-between mb-4">
          <h3 class="text-lg font-bold text-gray-900 dark:text-white">Policy Rules</h3>
          <button
            @click="addRule"
            class="px-3 py-1 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition text-sm flex items-center gap-1"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
            </svg>
            Add Rule
          </button>
        </div>

        <div v-if="editForm.rules?.length === 0" class="text-center py-8">
          <p class="text-gray-500 dark:text-gray-400 text-sm">No rules defined yet. Add your first rule.</p>
        </div>

        <div v-else class="space-y-2">
          <div
            v-for="(rule, idx) in editForm.rules"
            :key="idx"
            class="flex items-center gap-2 p-3 bg-gray-50 dark:bg-gray-700/50 rounded-lg"
          >
            <span class="flex-1 text-gray-900 dark:text-white">{{ rule }}</span>
            <button
              @click="removeRule(idx)"
              class="px-2 py-1 text-red-600 dark:text-red-400 hover:text-red-800 dark:hover:text-red-300 text-sm"
            >
              Remove
            </button>
          </div>
        </div>

        <!-- Add Rule Input -->
        <div v-if="showAddRule" class="mt-4 flex gap-2">
          <input
            v-model="newRule"
            type="text"
            placeholder="Enter rule description..."
            class="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-white focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            @keyup.enter="confirmAddRule"
          />
          <button
            @click="confirmAddRule"
            class="px-3 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition text-sm"
          >
            Add
          </button>
          <button
            @click="cancelAddRule"
            class="px-3 py-2 bg-gray-300 dark:bg-gray-600 text-gray-900 dark:text-white rounded-lg hover:bg-gray-400 dark:hover:bg-gray-500 transition text-sm"
          >
            Cancel
          </button>
        </div>
      </div>

      <!-- Target Devices -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
        <h3 class="text-lg font-bold text-gray-900 dark:text-white mb-4">Target Devices</h3>
        <div class="space-y-3">
          <div class="flex items-center">
            <input
              type="radio"
              id="allDevices"
              v-model="deviceTargetMode"
              value="all"
              class="rounded"
            />
            <label for="allDevices" class="ml-2 text-gray-700 dark:text-gray-300">
              Apply to all devices
            </label>
          </div>
          <div class="flex items-center">
            <input
              type="radio"
              id="selectedDevices"
              v-model="deviceTargetMode"
              value="selected"
              class="rounded"
            />
            <label for="selectedDevices" class="ml-2 text-gray-700 dark:text-gray-300">
              Apply to selected devices only
            </label>
          </div>
        </div>

        <div v-if="deviceTargetMode === 'selected'" class="mt-4">
          <p class="text-sm text-gray-600 dark:text-gray-400 mb-2">
            {{ editForm.targetDevices?.length || 0 }} device(s) selected
          </p>
          <div class="bg-gray-50 dark:bg-gray-700/50 rounded-lg p-4 max-h-60 overflow-y-auto">
            <p class="text-sm text-gray-500 dark:text-gray-400">
              Device selection UI can be expanded here to show device list and filtering options.
            </p>
          </div>
        </div>
      </div>

      <!-- Timestamps -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <!-- Created -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-2">Created</h3>
          <p class="text-gray-900 dark:text-white">{{ formatDateTime(policy.createdAt) }}</p>
          <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">{{ formatTimeAgo(policy.createdAt) }}</p>
        </div>

        <!-- Last Modified -->
        <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6">
          <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-300 uppercase tracking-wide mb-2">Last Modified</h3>
          <p class="text-gray-900 dark:text-white">{{ formatDateTime(policy.updatedAt) }}</p>
          <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">{{ formatTimeAgo(policy.updatedAt) }}</p>
        </div>
      </div>

      <!-- Action Buttons -->
      <div class="bg-white dark:bg-gray-800 rounded-lg shadow p-6 flex gap-3 justify-end">
        <router-link
          to="/policies"
          class="px-4 py-2 bg-gray-300 dark:bg-gray-600 text-gray-900 dark:text-white rounded-lg hover:bg-gray-400 dark:hover:bg-gray-500 transition"
        >
          Cancel
        </router-link>
        <button
          @click="savePolicyChanges"
          :disabled="saving"
          class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 transition"
        >
          {{ saving ? 'Saving...' : isNew ? 'Create Policy' : 'Save Changes' }}
        </button>
        <button
          v-if="!isNew && policy.status === 'Draft'"
          @click="activatePolicy"
          :disabled="saving"
          class="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:bg-gray-400 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
          </svg>
          {{ saving ? 'Activating...' : 'Activate' }}
        </button>
        <button
          v-else-if="!isNew && policy.status === 'Active'"
          @click="deactivatePolicy"
          :disabled="saving"
          class="px-4 py-2 bg-yellow-600 text-white rounded-lg hover:bg-yellow-700 disabled:bg-gray-400 transition flex items-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
          </svg>
          {{ saving ? 'Deactivating...' : 'Deactivate' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, reactive, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { usePolicyStore, type PolicyDetails } from '@stores/policyStore'

const route = useRoute()
const router = useRouter()
const policyStore = usePolicyStore()

const loading = ref(false)
const saving = ref(false)
const showAddRule = ref(false)
const newRule = ref('')
const deviceTargetMode = ref('all')

const policyId = computed(() => route.params.id as string)
const isNew = computed(() => policyId.value === 'new')

const policy = computed(() => {
  if (isNew.value) {
    return {
      policyId: 'new',
      name: '',
      description: '',
      status: 'Draft',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      enforcementLevel: 'Standard' as const,
      isActive: false,
      deviceCount: 0,
      rules: [],
      targetDevices: []
    } as PolicyDetails
  }
  return policyStore.getPolicyById(policyId.value)
})

const editForm = reactive<Partial<PolicyDetails>>({
  name: '',
  description: '',
  enforcementLevel: 'Standard',
  rules: [],
  targetDevices: []
})

function initializeForm() {
  if (!isNew.value && policy.value) {
    editForm.name = policy.value.name
    editForm.description = policy.value.description
    editForm.enforcementLevel = policy.value.enforcementLevel
    editForm.rules = [...(policy.value.rules || [])]
    editForm.targetDevices = [...(policy.value.targetDevices || [])]
  }
}

function addRule() {
  showAddRule.value = true
}

function confirmAddRule() {
  if (newRule.value.trim()) {
    if (!editForm.rules) editForm.rules = []
    editForm.rules.push(newRule.value.trim())
    newRule.value = ''
    showAddRule.value = false
  }
}

function cancelAddRule() {
  newRule.value = ''
  showAddRule.value = false
}

function removeRule(idx: number) {
  if (editForm.rules) {
    editForm.rules.splice(idx, 1)
  }
}

async function savePolicyChanges() {
  if (!editForm.name || !editForm.description) {
    return
  }

  saving.value = true
  try {
    if (isNew.value) {
      await policyStore.createPolicy({
        name: editForm.name,
        description: editForm.description,
        status: 'Draft',
        enforcementLevel: editForm.enforcementLevel || 'Standard',
        isActive: false,
        deviceCount: 0,
        rules: editForm.rules || [],
        targetDevices: editForm.targetDevices || []
      })
    } else if (policy.value) {
      await policyStore.updatePolicy(policy.value.policyId, {
        name: editForm.name,
        description: editForm.description,
        enforcementLevel: editForm.enforcementLevel,
        rules: editForm.rules,
        targetDevices: editForm.targetDevices
      })
    }
    router.push('/policies')
  } finally {
    saving.value = false
  }
}

async function activatePolicy() {
  if (!policy.value) return
  saving.value = true
  try {
    await policyStore.activatePolicy(policy.value.policyId)
  } finally {
    saving.value = false
  }
}

async function deactivatePolicy() {
  if (!policy.value) return
  saving.value = true
  try {
    await policyStore.deactivatePolicy(policy.value.policyId)
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
  if (!isNew.value && !policyStore.policies.length) {
    loading.value = true
    policyStore.fetchPolicies().then(() => {
      loading.value = false
      initializeForm()
    })
  } else if (!isNew.value && policy.value) {
    initializeForm()
  }
})
</script>
