import { createRouter, createWebHistory, RouteRecordRaw, NavigationGuardNext, RouteLocationNormalized } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'

const routes: Array<RouteRecordRaw> = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@views/LoginView.vue'),
    meta: { requiresAuth: false }
  },
  {
    path: '/',
    name: 'Dashboard',
    component: () => import('@views/DashboardView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/devices',
    name: 'Devices',
    component: () => import('@views/DevicesView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/devices/:id',
    name: 'DeviceDetail',
    component: () => import('@views/DeviceDetailView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/alerts',
    name: 'Alerts',
    component: () => import('@views/AlertsView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/alerts/:id',
    name: 'AlertDetail',
    component: () => import('@views/AlertDetailView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/policies',
    name: 'Policies',
    component: () => import('@views/PoliciesView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/policies/:id',
    name: 'PolicyDetail',
    component: () => import('@views/PolicyDetailView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/compliance-report',
    name: 'ComplianceReport',
    component: () => import('@views/ComplianceReportView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/releases',
    name: 'Releases',
    component: () => import('@views/ReleasesView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/releases/:id',
    name: 'ReleaseDetail',
    component: () => import('@views/ReleaseDetailView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/tags',
    name: 'Tags',
    component: () => import('@views/TagsView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/tags/:id',
    name: 'TagDetail',
    component: () => import('@views/TagDetailView.vue'),
    meta: { requiresAuth: true }
  },
  {
    path: '/settings',
    name: 'Settings',
    component: () => import('@views/SettingsView.vue'),
    meta: { requiresAuth: true }
  }
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
})

/**
 * Global authentication guard
 */
router.beforeEach(
  (to: RouteLocationNormalized, from: RouteLocationNormalized, next: NavigationGuardNext) => {
    const authStore = useAuthStore()

    // Initialize auth from localStorage on first load
    if (!from.name) {
      authStore.initialize()
    }

    const requiresAuth = to.meta.requiresAuth !== false
    const isAuthenticated = authStore.isAuthenticated

    // Redirect unauthenticated users to login
    if (requiresAuth && !isAuthenticated) {
      next({
        name: 'Login',
        query: { redirect: to.fullPath }
      })
      return
    }

    // Redirect authenticated users away from login page
    if (to.name === 'Login' && isAuthenticated) {
      next({ name: 'Dashboard' })
      return
    }

    next()
  }
)

export default router
