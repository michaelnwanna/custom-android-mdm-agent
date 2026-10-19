<template>
  <div class="min-h-screen bg-gray-50 dark:bg-gray-900">
    <!-- Show login layout for unauthenticated users -->
    <template v-if="!isAuthenticated && isLoginPage">
      <router-view />
    </template>

    <!-- Show dashboard layout for authenticated users -->
    <template v-else-if="isAuthenticated">
      <div class="flex">
        <!-- Sidebar -->
        <Sidebar />
        
        <!-- Main Content -->
        <div class="flex-1 flex flex-col">
          <!-- Header -->
          <Header />
          
          <!-- Page Content -->
          <main class="flex-1 overflow-auto">
            <div class="container mx-auto px-4 py-8">
              <router-view />
            </div>
          </main>
        </div>
      </div>
      
      <!-- Toast Container -->
      <ToastContainer />
    </template>

    <!-- Fallback: Show router-view during initial auth check -->
    <template v-else>
      <router-view />
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useRoute } from 'vue-router'
import Sidebar from '@components/Layout/Sidebar.vue'
import Header from '@components/Layout/Header.vue'
import ToastContainer from '@components/Layout/ToastContainer.vue'
import signalRService from '@services/signalRService'
import { useDeviceStore } from '@stores/deviceStore'
import { useTelemetryStore } from '@stores/telemetryStore'
import { useAlertStore } from '@stores/alertStore'
import { useAuthStore } from '@stores/authStore'
import toastService from '@services/toastService'

const route = useRoute()
const authStore = useAuthStore()
const deviceStore = useDeviceStore()
const telemetryStore = useTelemetryStore()
const alertStore = useAlertStore()

const isAuthenticated = computed(() => authStore.isAuthenticated)
const isLoginPage = computed(() => route.name === 'Login')

onMounted(async () => {
  // Initialize auth from localStorage
  authStore.initialize()

  // Only connect to SignalR if authenticated and not on login page
  if (authStore.isAuthenticated && route.name !== 'Login') {
    try {
      await signalRService.connect({
        onFleetMetricsUpdate: (metrics) => {
          telemetryStore.updateFleetMetrics(metrics)
        },
        onAlertReceived: (alert) => {
          alertStore.addAlert(alert)
          toastService.warning(`Alert: ${alert.message}`, 5000)
        },
        onDeviceStatusChanged: (device) => {
          const existingDevice = deviceStore.devices.find(d => d.id === device.id)
          if (existingDevice) {
            Object.assign(existingDevice, device)
          }
          toastService.info(`Device ${device.name} status updated`)
        },
        onConnectionStateChanged: (state) => {
          if (state === 'connected') {
            toastService.success('Connected to real-time service')
          } else if (state === 'disconnected') {
            toastService.error('Disconnected from real-time service')
          } else if (state === 'failed') {
            toastService.error('Failed to connect to real-time service, using local updates')
          }
        },
      })
    } catch (error) {
      console.error('Failed to connect to real-time service:', error)
    }
  }
})
</script>

<style scoped>
</style>
