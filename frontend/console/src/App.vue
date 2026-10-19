<template>
  <div class="min-h-screen bg-gray-50 dark:bg-gray-900">
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
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import Sidebar from '@components/Layout/Sidebar.vue'
import Header from '@components/Layout/Header.vue'
import ToastContainer from '@components/Layout/ToastContainer.vue'
import signalRService from '@services/signalRService'
import { useDeviceStore } from '@stores/deviceStore'
import { useTelemetryStore } from '@stores/telemetryStore'
import { useAlertStore } from '@stores/alertStore'
import toastService from '@services/toastService'

const deviceStore = useDeviceStore()
const telemetryStore = useTelemetryStore()
const alertStore = useAlertStore()

onMounted(async () => {
  // Initialize SignalR connection
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
})
</script>

<style scoped>
</style>
