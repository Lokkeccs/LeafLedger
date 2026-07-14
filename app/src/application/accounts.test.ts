import { describe, expect, it, vi } from 'vitest'
import type { ApiClient } from '../api/client'
import { getAccounts } from './accounts'

function fakeClient(result: unknown): ApiClient {
  return { GET: vi.fn().mockResolvedValue(result) } as unknown as ApiClient
}

describe('getAccounts', () => {
  it('maps the generated account catalog response', async () => {
    const account = {
      id: 'acc-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', isActive: true,
      groupId: 'group-1', validFrom: null, validTo: null, fxPolicy: null,
    }
    const client = fakeClient({ data: { spaceId: 'space-1', accounts: [account] } })

    await expect(getAccounts('space-1', client)).resolves.toEqual([account])
    expect(client.GET).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/accounts', { params: { path: { spaceId: 'space-1' } } })
  })

  it('throws when the generated client returns no data', async () => {
    await expect(getAccounts('space-1', fakeClient({ error: {} }))).rejects.toThrow('Failed to fetch accounts')
  })
})