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
  </div>
</template>

<script setup lang="ts">
import Sidebar from '@components/Layout/Sidebar.vue'
import Header from '@components/Layout/Header.vue'
</script>

<style scoped>
</style>
