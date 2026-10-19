<template>
  <aside class="w-64 bg-white dark:bg-gray-800 border-r border-gray-200 dark:border-gray-700 h-screen overflow-y-auto sticky top-0">
    <!-- Logo -->
    <div class="p-6 border-b border-gray-200 dark:border-gray-700">
      <div class="flex items-center space-x-2">
        <div class="w-8 h-8 bg-primary rounded-lg flex items-center justify-center">
          <span class="text-white font-bold">F</span>
        </div>
        <h1 class="text-xl font-bold text-gray-900 dark:text-white">Fleet</h1>
      </div>
    </div>

    <!-- Navigation -->
    <nav class="p-4 space-y-2">
      <router-link
        v-for="item in navigationItems"
        :key="item.name"
        :to="item.path"
        class="flex items-center space-x-3 px-4 py-2 rounded-lg transition-colors"
        :class="{ 'bg-primary text-white': $route.path === item.path, 'text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700': $route.path !== item.path }"
      >
        <component :is="item.icon" class="w-5 h-5" />
        <span>{{ item.name }}</span>
      </router-link>
    </nav>

    <!-- Settings Section -->
    <div class="absolute bottom-0 w-64 border-t border-gray-200 dark:border-gray-700 p-4">
      <router-link
        to="/settings"
        class="flex items-center space-x-3 px-4 py-2 rounded-lg text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors"
      >
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
        </svg>
        <span>Settings</span>
      </router-link>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { ref } from 'vue'

const navigationItems = ref([
  { name: 'Dashboard', path: '/', icon: 'svg-icon-dashboard' },
  { name: 'Devices', path: '/devices', icon: 'svg-icon-devices' },
  { name: 'Alerts', path: '/alerts', icon: 'svg-icon-alerts' },
  { name: 'Policies', path: '/policies', icon: 'svg-icon-policies' },
  { name: 'Compliance', path: '/compliance-report', icon: 'svg-icon-compliance' },
  { name: 'Releases', path: '/releases', icon: 'svg-icon-releases' },
  { name: 'Tags', path: '/tags', icon: 'svg-icon-tags' }
])
</script>

<style scoped>
</style>
