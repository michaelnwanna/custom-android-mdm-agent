<template>
  <div class="space-y-3">
    <div v-if="loading" class="space-y-2">
      <div v-for="i in 3" :key="i" class="h-16 bg-gray-100 dark:bg-gray-700 rounded animate-pulse" />
    </div>
    <div v-else-if="error" class="text-red-600 dark:text-red-300 text-sm">{{ error }}</div>
    <div v-else-if="atRiskDevices.length === 0" class="text-gray-500 text-center py-8">
      All devices are healthy ✓
    </div>
    <div v-else>
      <div
        v-for="device in atRiskDevices.slice(0, 5)"
        :key="device.deviceId"
        class="flex items-center justify-between p-4 bg-gray-50 dark:bg-gray-700 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-600 transition-colors cursor-pointer"
        @click="navigateToDevice(device.deviceId)"
      >
        <div class="flex-1">
          <p class="font-medium text-gray-900 dark:text-white">{{ device.deviceName }}</p>
          <p class="text-sm text-gray-600 dark:text-gray-400">{{ device.riskReason }}</p>
        </div>
        <span :class="['px-3 py-1 rounded-full text-sm font-medium text-white', getSeverityBgClass(device.riskLevel)]">
          {{ device.riskLevel }}
        </span>
      </div>
      <router-link
        to="/alerts"
        class="block mt-4 text-center text-primary hover:underline text-sm font-medium"
      >
        View All At-Risk Devices →
      </router-link>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'

interface AtRiskDevice {
  deviceId: string
  deviceName: string
  riskLevel: 'Critical' | 'High' | 'Medium' | 'Low'
  riskReason: string
  batteryPercentage?: number
  connectivity?: string
  lastSeen?: string
}

const router = useRouter()
const loading = ref(false)
const error = ref<string | null>(null)

// Mock at-risk devices data (would come from backend /api/fleet/health/at-risk)
const atRiskDevices = computed((): AtRiskDevice[] => {
  // Mock data for now - will integrate with real API later
  return [
    {
      deviceId: 'device-001',
      deviceName: 'Device-001',
      riskLevel: 'Critical',
      riskReason: 'Low Battery (5%)',
      batteryPercentage: 5,
      connectivity: 'WiFi'
    },
    {
      deviceId: 'device-005',
      deviceName: 'Device-005',
      riskLevel: 'High',
      riskReason: 'No Connectivity',
      connectivity: 'None'
    },
    {
      deviceId: 'device-012',
      deviceName: 'Device-012',
      riskLevel: 'High',
      riskReason: 'High Memory Usage (92%)',
      connectivity: 'Cellular'
    },
    {
      deviceId: 'device-018',
      deviceName: 'Device-018',
      riskLevel: 'Medium',
      riskReason: 'Low Storage (8%)',
      connectivity: 'WiFi'
    },
    {
      deviceId: 'device-023',
      deviceName: 'Device-023',
      riskLevel: 'Medium',
      riskReason: 'Device Offline (45min)',
      connectivity: 'None'
    }
  ]
})

function getSeverityBgClass(severity: string): string {
  switch (severity) {
    case 'Critical':
      return 'bg-red-600'
    case 'High':
      return 'bg-orange-500'
    case 'Medium':
      return 'bg-amber-500'
    default:
      return 'bg-blue-500'
  }
}

function navigateToDevice(deviceId: string) {
  router.push(`/devices/${deviceId}`)
}

onMounted(async () => {
  loading.value = true
  try {
    // TODO: Fetch from telemetryStore.fetchAtRiskDevices()
    // For now using mock data
    error.value = null
  } catch (err) {
    error.value = 'Failed to load at-risk devices'
  } finally {
    loading.value = false
  }
})
</script>
