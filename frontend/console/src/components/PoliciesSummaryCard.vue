<template>
  <div class="bg-white dark:bg-gray-800 rounded-lg shadow-lg overflow-hidden hover:shadow-xl transition-shadow">
    <!-- Header -->
    <div class="bg-gradient-to-r from-blue-500 to-blue-600 dark:from-blue-700 dark:to-blue-800 px-6 py-4">
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-3">
          <div class="bg-white dark:bg-blue-900 p-2 rounded-lg">
            <svg class="w-6 h-6 text-blue-600 dark:text-blue-200" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </div>
          <h3 class="text-white font-semibold text-lg">Policies</h3>
        </div>
        <router-link
          to="/policies"
          class="text-white hover:bg-white hover:bg-opacity-20 px-3 py-1 rounded text-sm font-medium transition"
        >
          View All →
        </router-link>
      </div>
    </div>

    <!-- Content -->
    <div class="p-6 space-y-6">
      <!-- Metrics Grid -->
      <div class="grid grid-cols-3 gap-4">
        <!-- Total Policies -->
        <div class="bg-gradient-to-br from-blue-50 to-blue-100 dark:from-blue-900/30 dark:to-blue-800/30 p-4 rounded-lg">
          <p class="text-xs text-blue-600 dark:text-blue-400 font-medium mb-1">Total</p>
          <p class="text-2xl font-bold text-blue-900 dark:text-blue-100">{{ totalPolicies }}</p>
        </div>

        <!-- Active Policies -->
        <div class="bg-gradient-to-br from-green-50 to-green-100 dark:from-green-900/30 dark:to-green-800/30 p-4 rounded-lg">
          <p class="text-xs text-green-600 dark:text-green-400 font-medium mb-1">Active</p>
          <p class="text-2xl font-bold text-green-900 dark:text-green-100">{{ activePolicies }}</p>
        </div>

        <!-- Avg Compliance -->
        <div class="bg-gradient-to-br from-purple-50 to-purple-100 dark:from-purple-900/30 dark:to-purple-800/30 p-4 rounded-lg border-2" :class="getComplianceBorderClass">
          <p class="text-xs text-purple-600 dark:text-purple-400 font-medium mb-1">Avg Compliance</p>
          <p class="text-2xl font-bold" :class="getComplianceColorClass">{{ averageCompliance }}%</p>
        </div>
      </div>

      <!-- Overall Compliance Gauge -->
      <div class="space-y-2">
        <p class="text-sm font-semibold text-gray-700 dark:text-gray-300">Overall Compliance</p>
        <div class="flex items-center gap-4">
          <div class="flex-1">
            <div class="flex items-center justify-between mb-1">
              <span class="text-xs text-gray-600 dark:text-gray-400">Devices Compliant</span>
              <span class="text-xs font-semibold text-gray-900 dark:text-white">{{ totalCompliantDevices }}/{{ totalDevices }}</span>
            </div>
            <div class="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-3 overflow-hidden">
              <div
                class="h-full transition-all"
                :class="getComplianceBarClass"
                :style="{ width: compliancePercentage + '%' }"
              />
            </div>
            <p class="text-xs text-gray-600 dark:text-gray-400 mt-1">{{ compliancePercentage }}% compliant</p>
          </div>
        </div>
      </div>

      <!-- Policy Type Distribution -->
      <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
        <p class="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">By Type</p>
        <div class="space-y-2">
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">🔒 Security</span>
            <span class="text-sm font-semibold text-gray-900 dark:text-white">{{ securityPolicies }}</span>
          </div>
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">📋 Compliance</span>
            <span class="text-sm font-semibold text-gray-900 dark:text-white">{{ compliancePolicies }}</span>
          </div>
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">⚡ Performance</span>
            <span class="text-sm font-semibold text-gray-900 dark:text-white">{{ performancePolicies }}</span>
          </div>
          <div class="flex items-center justify-between">
            <span class="text-xs text-gray-600 dark:text-gray-400">🔧 Custom</span>
            <span class="text-sm font-semibold text-gray-900 dark:text-white">{{ customPolicies }}</span>
          </div>
        </div>
      </div>

      <!-- Recent Deployments -->
      <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
        <p class="text-xs font-semibold text-gray-700 dark:text-gray-300 mb-3">Recent Activity</p>
        <div v-if="recentPolicies.length > 0" class="space-y-2">
          <div
            v-for="policy in recentPolicies.slice(0, 3)"
            :key="policy.policyId"
            class="flex items-start gap-2 p-2 bg-gray-50 dark:bg-gray-700/50 rounded-lg"
          >
            <div class="flex-shrink-0 w-2 h-2 rounded-full mt-1.5 bg-blue-500"></div>
            <div class="flex-1 min-w-0">
              <p class="text-xs font-medium text-gray-900 dark:text-white truncate">{{ policy.name }}</p>
              <p class="text-xs text-gray-500 dark:text-gray-400">{{ policy.deviceCount }} devices · {{ policy.compliancePercentage }}% compliance</p>
            </div>
          </div>
        </div>
        <div v-else class="text-center py-2 text-xs text-gray-500 dark:text-gray-400">
          No recent activity
        </div>
      </div>

      <!-- Status Badges -->
      <div class="border-t border-gray-200 dark:border-gray-700 pt-4">
        <p class="text-xs font-semibold text-gray-700 dark:text-gray-300 mb-2">Deployment Status</p>
        <div class="flex gap-2 flex-wrap">
          <span class="inline-flex items-center gap-1 px-3 py-1 bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-200 rounded-full text-xs font-medium">
            <span class="w-2 h-2 bg-green-600 dark:bg-green-400 rounded-full"></span>
            {{ deploying }} Deploying
          </span>
          <span class="inline-flex items-center gap-1 px-3 py-1 bg-yellow-100 dark:bg-yellow-900/30 text-yellow-800 dark:text-yellow-200 rounded-full text-xs font-medium">
            <span class="w-2 h-2 bg-yellow-600 dark:bg-yellow-400 rounded-full"></span>
            {{ pendingReview }} Pending
          </span>
        </div>
      </div>
    </div>

    <!-- Footer -->
    <div class="bg-gray-50 dark:bg-gray-700/50 px-6 py-3 border-t border-gray-200 dark:border-gray-700 flex items-center justify-between">
      <p class="text-xs text-gray-600 dark:text-gray-400">
        Last updated: {{ formatTime(lastUpdated) }}
      </p>
      <router-link
        to="/policies/new"
        class="text-xs text-blue-600 dark:text-blue-400 hover:underline font-medium"
      >
        + New Policy
      </router-link>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { usePolicies } from '@/composables/usePolicies'
import signalRService from '@services/signalRService'

const { policies, fetchPolicies } = usePolicies({ autoLoad: false })

const lastUpdated = ref(new Date().toISOString())

const totalPolicies = computed(() => policies.value.length)

const activePolicies = computed(() =>
  policies.value.filter(p => p.status === 'Active').length
)

const totalDevices = computed(() =>
  policies.value.reduce((sum, p) => sum + (p.deviceCount || 0), 0)
)

const totalCompliantDevices = computed(() =>
  policies.value.reduce((sum, p) => sum + (p.compliantDeviceCount || 0), 0)
)

const averageCompliance = computed(() => {
  if (policies.value.length === 0) return 0
  const total = policies.value.reduce((sum, p) => sum + (p.compliancePercentage || 0), 0)
  return Math.round(total / policies.value.length)
})

const compliancePercentage = computed(() => {
  if (totalDevices.value === 0) return 0
  return Math.round((totalCompliantDevices.value / totalDevices.value) * 100)
})

const securityPolicies = computed(() =>
  policies.value.filter(p => p.type === 'Security').length
)

const compliancePolicies = computed(() =>
  policies.value.filter(p => p.type === 'Compliance').length
)

const performancePolicies = computed(() =>
  policies.value.filter(p => p.type === 'Performance').length
)

const customPolicies = computed(() =>
  policies.value.filter(p => p.type === 'Custom').length
)

const recentPolicies = computed(() =>
  policies.value.slice().reverse().slice(0, 5)
)

const deploying = computed(() =>
  policies.value.filter(p => p.status === 'Active' && (p.compliancePercentage || 0) < 90).length
)

const pendingReview = computed(() =>
  policies.value.filter(p => p.status === 'Draft').length
)

const getComplianceBarClass = computed(() => {
  if (compliancePercentage.value >= 90) return 'bg-green-500'
  if (compliancePercentage.value >= 70) return 'bg-yellow-500'
  return 'bg-red-500'
})

const getComplianceColorClass = computed(() => {
  if (averageCompliance.value >= 90) return 'text-green-600 dark:text-green-400'
  if (averageCompliance.value >= 70) return 'text-yellow-600 dark:text-yellow-400'
  return 'text-red-600 dark:text-red-400'
})

const getComplianceBorderClass = computed(() => {
  if (averageCompliance.value >= 90) return 'border-green-300 dark:border-green-700'
  if (averageCompliance.value >= 70) return 'border-yellow-300 dark:border-yellow-700'
  return 'border-red-300 dark:border-red-700'
})

const formatTime = (dateString: string): string => {
  const date = new Date(dateString)
  const now = new Date()
  const diff = now.getTime() - date.getTime()
  const minutes = Math.floor(diff / 60000)

  if (minutes < 1) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  return date.toLocaleDateString()
}

onMounted(async () => {
  await fetchPolicies()

  // Set up auto-refresh
  const refreshInterval = setInterval(async () => {
    await fetchPolicies()
    lastUpdated.value = new Date().toISOString()
  }, 30000) // Refresh every 30 seconds

  // SignalR real-time updates
  await signalRService.connect({
    onPolicyUpdated: () => {
      fetchPolicies()
      lastUpdated.value = new Date().toISOString()
    },
    onPolicyComplianceChanged: () => {
      fetchPolicies()
      lastUpdated.value = new Date().toISOString()
    }
  })

  onUnmounted(() => {
    clearInterval(refreshInterval)
  })
})
</script>
