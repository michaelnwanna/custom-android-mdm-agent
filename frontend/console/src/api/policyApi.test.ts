import { beforeEach, describe, expect, it, vi } from 'vitest'

const { get, post } = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn()
}))

vi.mock('@/api/client', () => ({
  apiClient: { get, post }
}))

import {
  assignPolicy,
  fetchAssignableDevices,
  fetchComplianceReport,
  fetchPolicyById
} from './policyApi'
import { API_ENDPOINTS } from './endpoints'

describe('policy management API', () => {
  beforeEach(() => {
    get.mockReset()
    post.mockReset()
  })

  it('assigns policies using one backend-supported enrollment request per device', async () => {
    post.mockResolvedValue({
      assignmentId: 'assignment-1',
      assignedVersion: 3,
      assignedAt: '2026-10-19T12:00:00.000Z'
    })

    const results = await assignPolicy('policy-guid', {
      enrollmentIds: ['enrollment-1', 'enrollment-2']
    })

    expect(post).toHaveBeenCalledTimes(2)
    expect(post).toHaveBeenNthCalledWith(1, API_ENDPOINTS.POLICIES.ASSIGN, {
      enrollmentId: 'enrollment-1',
      policyId: 'policy-guid'
    })
    expect(results).toHaveLength(2)
    expect(results.every(result => !result.error)).toBe(true)
  })

  it('keeps per-device errors when only some assignments fail', async () => {
    post
      .mockResolvedValueOnce({ assignmentId: 'assignment-1', assignedVersion: 1, assignedAt: 'now' })
      .mockRejectedValueOnce(new Error('Device is not enrolled'))

    const results = await assignPolicy('policy-guid', {
      enrollmentIds: ['enrollment-1', 'enrollment-2']
    })

    expect(results[0]?.assignmentId).toBe('assignment-1')
    expect(results[1]?.error).toBe('Device is not enrolled')
  })

  it('loads assignable inventory and compliance from their backend endpoints', async () => {
    get.mockResolvedValue([])

    await fetchAssignableDevices()
    await fetchComplianceReport()

    expect(get).toHaveBeenNthCalledWith(1, API_ENDPOINTS.POLICIES.ASSIGNABLE_DEVICES)
    expect(get).toHaveBeenNthCalledWith(2, API_ENDPOINTS.POLICIES.COMPLIANCE_REPORT)
  })

  it('propagates failures to load a policy instead of masking them as not found', async () => {
    const error = new Error('Service unavailable')
    get.mockRejectedValue(error)

    await expect(fetchPolicyById('policy-guid')).rejects.toBe(error)
    expect(get).toHaveBeenCalledWith(API_ENDPOINTS.POLICIES.GET('policy-guid'))
  })
})
