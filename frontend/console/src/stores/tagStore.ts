import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Tag } from '@/types'

export const useTagStore = defineStore('tags', () => {
  const tags = ref<Tag[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // Color palette for tags
  const colors = [
    '#EF4444', // red-500
    '#F97316', // orange-500
    '#EAB308', // yellow-500
    '#22C55E', // green-500
    '#06B6D4', // cyan-500
    '#3B82F6', // blue-500
    '#8B5CF6', // violet-500
    '#EC4899'  // pink-500
  ]

  // Tag types for realistic data
  const tagTypes = [
    'Production',
    'Testing',
    'Development',
    'High-Priority',
    'Low-Priority',
    'Critical',
    'Department-IT',
    'Department-Finance',
    'Location-HQ',
    'Location-Branch',
    'Needs-Update',
    'Compliance-Required',
    'New-Device',
    'Legacy-System',
    'Contract-Temp'
  ]

  function generateMockTags(count: number): Tag[] {
    const mockTags: Tag[] = []
    const now = new Date()

    for (let i = 1; i <= count; i++) {
      const tagType = tagTypes[Math.floor(Math.random() * tagTypes.length)]
      mockTags.push({
        tagId: `tag-${String(i).padStart(3, '0')}`,
        name: `${tagType}-${Math.floor(Math.random() * 100)}`,
        description: `${tagType} tag for device management and organization. Created on ${now.toLocaleDateString()}`,
        color: colors[Math.floor(Math.random() * colors.length)],
        deviceCount: Math.floor(Math.random() * 150) + 1
      })
    }

    return mockTags
  }

  async function fetchTags() {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch('/api/tags')
      
      if (!response.ok) throw new Error('Failed to fetch tags')
      
      const data = await response.json()
      tags.value = data.items || data
    } catch (err) {
      // Fallback to mock data
      tags.value = generateMockTags(25)
    } finally {
      loading.value = false
    }
  }

  async function createTag(tag: Omit<Tag, 'tagId'>) {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch('/api/tags', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(tag)
      })

      if (!response.ok) throw new Error('Failed to create tag')

      const newTag = await response.json()
      tags.value.push(newTag)
      return newTag
    } catch (err) {
      // Create tag locally with mock ID
      const mockTag: Tag = {
        ...tag,
        tagId: `tag-${Date.now()}`
      }
      tags.value.push(mockTag)
      return mockTag
    } finally {
      loading.value = false
    }
  }

  async function updateTag(tagId: string, updates: Partial<Tag>) {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch(`/api/tags/${tagId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(updates)
      })

      if (!response.ok) throw new Error('Failed to update tag')

      const updatedTag = await response.json()
      const index = tags.value.findIndex(t => t.tagId === tagId)
      if (index !== -1) {
        tags.value[index] = updatedTag
      }
      return updatedTag
    } catch (err) {
      // Update tag locally
      const index = tags.value.findIndex(t => t.tagId === tagId)
      if (index !== -1) {
        tags.value[index] = { ...tags.value[index], ...updates }
        return tags.value[index]
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  async function deleteTag(tagId: string) {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch(`/api/tags/${tagId}`, {
        method: 'DELETE'
      })

      if (!response.ok) throw new Error('Failed to delete tag')

      tags.value = tags.value.filter(t => t.tagId !== tagId)
    } catch (err) {
      // Delete tag locally
      tags.value = tags.value.filter(t => t.tagId !== tagId)
    } finally {
      loading.value = false
    }
  }

  async function assignTagsToDevices(tagIds: string[], deviceIds: string[]) {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch('/api/tags/assign', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tagIds, deviceIds })
      })

      if (!response.ok) throw new Error('Failed to assign tags')

      // Update device counts
      tagIds.forEach(tagId => {
        const tag = tags.value.find(t => t.tagId === tagId)
        if (tag) {
          tag.deviceCount += deviceIds.length
        }
      })
    } catch (err) {
      // Handle locally
      tagIds.forEach(tagId => {
        const tag = tags.value.find(t => t.tagId === tagId)
        if (tag) {
          tag.deviceCount += deviceIds.length
        }
      })
    } finally {
      loading.value = false
    }
  }

  async function removeTagsFromDevices(tagIds: string[], deviceIds: string[]) {
    loading.value = true
    error.value = null

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch('/api/tags/unassign', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tagIds, deviceIds })
      })

      if (!response.ok) throw new Error('Failed to remove tags')

      // Update device counts
      tagIds.forEach(tagId => {
        const tag = tags.value.find(t => t.tagId === tagId)
        if (tag) {
          tag.deviceCount = Math.max(0, tag.deviceCount - deviceIds.length)
        }
      })
    } catch (err) {
      // Handle locally
      tagIds.forEach(tagId => {
        const tag = tags.value.find(t => t.tagId === tagId)
        if (tag) {
          tag.deviceCount = Math.max(0, tag.deviceCount - deviceIds.length)
        }
      })
    } finally {
      loading.value = false
    }
  }

  async function getTagById(tagId: string) {
    const tag = tags.value.find(t => t.tagId === tagId)
    if (tag) return tag

    try {
      // TODO: Replace with actual API endpoint
      const response = await fetch(`/api/tags/${tagId}`)
      if (!response.ok) throw new Error('Tag not found')
      return await response.json()
    } catch (err) {
      return null
    }
  }

  // Computed properties for filtering and sorting
  const sortedTags = computed(() => {
    return [...tags.value].sort((a, b) => b.deviceCount - a.deviceCount)
  })

  const tagsByDeviceCount = computed(() => {
    return sortedTags.value
  })

  return {
    tags,
    loading,
    error,
    fetchTags,
    createTag,
    updateTag,
    deleteTag,
    assignTagsToDevices,
    removeTagsFromDevices,
    getTagById,
    sortedTags,
    tagsByDeviceCount,
    colors
  }
})
