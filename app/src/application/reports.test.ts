import { describe, expect, it, vi } from 'vitest'
import type { ApiClient } from '../api/client'
import { getTrialBalance } from './reports'

function fakeClient(result: unknown): ApiClient {
  return { GET: vi.fn().mockResolvedValue(result) } as unknown as ApiClient
}

describe('getTrialBalance', () => {
  it('maps the generated report response without changing minor units', async () => {
    const row = { accountId: 'acc-1', accountCode: 1000, accountName: 'Cash', accountKind: 'Asset', baseBalanceMinor: 12345 }
    const client = fakeClient({ data: { spaceId: 'space-1', lines: [row], totalBaseBalanceMinor: 0 } })

    await expect(getTrialBalance('space-1', client)).resolves.toEqual({ spaceId: 'space-1', rows: [row], totalBaseBalanceMinor: 0 })
    expect(client.GET).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/reports/trial-balance', { params: { path: { spaceId: 'space-1' } } })
  })

  it('throws when the generated client returns no data', async () => {
    await expect(getTrialBalance('space-1', fakeClient({ error: {} }))).rejects.toThrow('Failed to fetch trial balance')
  })
})