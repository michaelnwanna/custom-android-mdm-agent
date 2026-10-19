import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import apiClient from '@services/apiClient'
import type { Release } from '@types'

export interface ReleaseDetails extends Release {
  changelog: string
  deploymentGroups?: string[]
  downloadCount: number
  installationCount: number
  errorCount: number
  deploymentStatus: 'Pending' | 'In Progress' | 'Completed' | 'Failed'
  priority: 'Low' | 'Medium' | 'High' | 'Critical'
  minAndroidVersion: string
  maxAndroidVersion: string
  targetDeviceCount: number
  installedDeviceCount: number
}

function generateMockReleases(count: number = 25): ReleaseDetails[] {
  const appNames = [
    'com.example.mdm.agent',
    'com.example.mdm.securitysuite',
    'com.example.mdm.compliance',
    'com.example.mdm.monitoring',
    'com.example.mdm.analytics'
  ]

  const changelogTemplates = [
    'Security patches, bug fixes, and performance improvements',
    'Added biometric authentication support, fixed UI issues',
    'Enhanced encryption, improved battery life',
    'Major stability improvements and device compatibility updates',
    'New features: offline mode, advanced analytics, better sync',
    'Critical security update, recommended for all devices',
    'Performance optimization, reduced app size by 25%',
    'Improved device management features and reporting',
    'Bug fixes for Android 14 compatibility',
    'New dashboard features and improved user interface'
  ]

  const releases: ReleaseDetails[] = []
  for (let i = 0; i < count; i++) {
    const appName = appNames[i % appNames.length]
    const versionCode = Math.floor(Math.random() * 500) + 100
    const versionName = `${Math.floor(versionCode / 100)}.${(versionCode % 100) / 10}.${versionCode % 10}`
    const status = ['Draft', 'Published', 'Deprecated'][Math.floor(Math.random() * 3)] as 'Draft' | 'Published' | 'Deprecated'
    const deploymentStatus = (['Pending', 'In Progress', 'Completed', 'Failed'] as const)[Math.floor(Math.random() * 4)]
    const priority = (['Low', 'Medium', 'High', 'Critical'] as const)[Math.floor(Math.random() * 4)]
    const fileSize = Math.floor(Math.random() * 150) + 20 // MB
    const releaseDate = new Date(Date.now() - Math.random() * 90 * 24 * 60 * 60 * 1000)
    const downloadUrl = `https://example.com/releases/${appName}/${versionName}/app-release.apk`

    const targetDeviceCount = Math.floor(Math.random() * 300) + 50
    const installedDeviceCount = Math.floor(Math.random() * targetDeviceCount)
    const downloadCount = Math.floor(Math.random() * targetDeviceCount * 2)
    const installationCount = installedDeviceCount
    const errorCount = Math.floor(Math.random() * 50)

    releases.push({
      releaseId: `release-${String(i + 1).padStart(6, '0')}`,
      packageName: appName,
      versionCode,
      versionName,
      releaseDate: releaseDate.toISOString(),
      fileSize,
      downloadUrl,
      status,
      changelog: changelogTemplates[Math.floor(Math.random() * changelogTemplates.length)],
      deploymentGroups: [
        'Standard',
        'Beta Testers',
        'Enterprise',
        'Development'
      ].slice(0, Math.floor(Math.random() * 3) + 1),
      downloadCount,
      installationCount,
      errorCount,
      deploymentStatus,
      priority,
      minAndroidVersion: ['11', '12', '13', '14'][Math.floor(Math.random() * 4)],
      maxAndroidVersion: '14',
      targetDeviceCount,
      installedDeviceCount
    })
  }

  return releases
}

export const useReleaseStore = defineStore('release', () => {
  const releases = ref<ReleaseDetails[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  const publishedReleases = computed(() =>
    releases.value.filter(r => r.status === 'Published')
  )

  const draftReleases = computed(() =>
    releases.value.filter(r => r.status === 'Draft')
  )

  const deprecatedReleases = computed(() =>
    releases.value.filter(r => r.status === 'Deprecated')
  )

  const releasesByDeploymentStatus = computed(() => {
    const grouped: Record<string, ReleaseDetails[]> = {
      'Pending': [],
      'In Progress': [],
      'Completed': [],
      'Failed': []
    }
    releases.value.forEach(release => {
      grouped[release.deploymentStatus].push(release)
    })
    return grouped
  })

  const releasesByPriority = computed(() => {
    const grouped: Record<string, ReleaseDetails[]> = {
      'Critical': [],
      'High': [],
      'Medium': [],
      'Low': []
    }
    releases.value.forEach(release => {
      grouped[release.priority].push(release)
    })
    return grouped
  })

  async function fetchReleases() {
    loading.value = true
    error.value = null
    
    try {
      const response = await apiClient.get<ReleaseDetails[]>('/releases')
      releases.value = response.data
    } catch (err) {
      // Fallback to mock data
      releases.value = generateMockReleases(25)
      error.value = null
    } finally {
      loading.value = false
    }
  }

  async function createRelease(release: Omit<ReleaseDetails, 'releaseId' | 'releaseDate'>) {
    try {
      // TODO: API endpoint - POST /releases
      const response = await apiClient.post<ReleaseDetails>('/releases', release)
      releases.value.push(response.data)
      return response.data
    } catch (err) {
      // Fallback: create locally
      const newRelease: ReleaseDetails = {
        ...release,
        releaseId: `release-${Date.now()}`,
        releaseDate: new Date().toISOString()
      }
      releases.value.push(newRelease)
      return newRelease
    }
  }

  async function updateRelease(releaseId: string, updates: Partial<ReleaseDetails>) {
    try {
      // TODO: API endpoint - PUT /releases/{releaseId}
      await apiClient.put(`/releases/${releaseId}`, updates)
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        Object.assign(release, updates)
      }
    } catch (err) {
      // Fallback: update locally
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        Object.assign(release, updates)
      }
    }
  }

  async function deleteRelease(releaseId: string) {
    try {
      // TODO: API endpoint - DELETE /releases/{releaseId}
      await apiClient.delete(`/releases/${releaseId}`)
      const index = releases.value.findIndex(r => r.releaseId === releaseId)
      if (index > -1) {
        releases.value.splice(index, 1)
      }
    } catch (err) {
      // Fallback: delete locally
      const index = releases.value.findIndex(r => r.releaseId === releaseId)
      if (index > -1) {
        releases.value.splice(index, 1)
      }
    }
  }

  async function publishRelease(releaseId: string) {
    try {
      // TODO: API endpoint - POST /releases/{releaseId}/publish
      await apiClient.post(`/releases/${releaseId}/publish`, {})
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        release.status = 'Published'
        release.deploymentStatus = 'In Progress'
      }
    } catch (err) {
      // Fallback: update locally
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        release.status = 'Published'
        release.deploymentStatus = 'In Progress'
      }
    }
  }

  async function deprecateRelease(releaseId: string) {
    try {
      // TODO: API endpoint - POST /releases/{releaseId}/deprecate
      await apiClient.post(`/releases/${releaseId}/deprecate`, {})
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        release.status = 'Deprecated'
      }
    } catch (err) {
      // Fallback: update locally
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        release.status = 'Deprecated'
      }
    }
  }

  async function deployRelease(releaseId: string, targetGroups?: string[]) {
    try {
      // TODO: API endpoint - POST /releases/{releaseId}/deploy
      await apiClient.post(`/releases/${releaseId}/deploy`, { targetGroups })
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        release.deploymentStatus = 'In Progress'
      }
    } catch (err) {
      // Fallback: update locally
      const release = releases.value.find(r => r.releaseId === releaseId)
      if (release) {
        release.deploymentStatus = 'In Progress'
      }
    }
  }

  function getReleaseById(releaseId: string): ReleaseDetails | undefined {
    return releases.value.find(r => r.releaseId === releaseId)
  }

  function clearError() {
    error.value = null
  }

  return {
    releases,
    loading,
    error,
    publishedReleases,
    draftReleases,
    deprecatedReleases,
    releasesByDeploymentStatus,
    releasesByPriority,
    fetchReleases,
    createRelease,
    updateRelease,
    deleteRelease,
    publishRelease,
    deprecateRelease,
    deployRelease,
    getReleaseById,
    clearError
  }
})
