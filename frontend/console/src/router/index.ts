import { createRouter, createWebHistory, RouteRecordRaw } from 'vue-router'

const routes: Array<RouteRecordRaw> = [
  {
    path: '/',
    name: 'Dashboard',
    component: () => import('@views/DashboardView.vue')
  },
  {
    path: '/devices',
    name: 'Devices',
    component: () => import('@views/DevicesView.vue')
  },
  {
    path: '/devices/:id',
    name: 'DeviceDetail',
    component: () => import('@views/DeviceDetailView.vue')
  },
  {
    path: '/alerts',
    name: 'Alerts',
    component: () => import('@views/AlertsView.vue')
  },
  {
    path: '/alerts/:id',
    name: 'AlertDetail',
    component: () => import('@views/AlertDetailView.vue')
  },
  {
    path: '/policies',
    name: 'Policies',
    component: () => import('@views/PoliciesView.vue')
  },
  {
    path: '/policies/:id',
    name: 'PolicyDetail',
    component: () => import('@views/PolicyDetailView.vue')
  },
  {
    path: '/releases',
    name: 'Releases',
    component: () => import('@views/ReleasesView.vue')
  },
  {
    path: '/releases/:id',
    name: 'ReleaseDetail',
    component: () => import('@views/ReleaseDetailView.vue')
  },
  {
    path: '/tags',
    name: 'Tags',
    component: () => import('@views/TagsView.vue')
  },
  {
    path: '/tags/:id',
    name: 'TagDetail',
    component: () => import('@views/TagDetailView.vue')
  },
  {
    path: '/settings',
    name: 'Settings',
    component: () => import('@views/SettingsView.vue')
  }
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
})

export default router
