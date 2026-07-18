import { describe, expect, it, vi } from 'vitest'
import type { ApiClient } from '../api/client'
import { getBalanceSheet, getIncomeStatement, getTrialBalance } from './reports'

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

describe('statement report mappings', () => {
  const line = { accountId: null, accountCode: null, name: 'Current result', accountKind: 'Equity', amountMinor: -1250, isDerived: true }

  it('maps the balance-sheet response and preserves derived minor units', async () => {
    const client = fakeClient({ data: { spaceId: 'space-1', lines: [line], currentResultMinor: -1250 } })
    await expect(getBalanceSheet('space-1', client)).resolves.toEqual({ spaceId: 'space-1', lines: [line], currentResultMinor: -1250 })
    expect(client.GET).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/reports/balance-sheet', { params: { path: { spaceId: 'space-1' } } })
  })

  it('maps the income-statement response and throws when data is absent', async () => {
    const client = fakeClient({ data: { spaceId: 'space-1', lines: [line], netResultMinor: -1250 } })
    await expect(getIncomeStatement('space-1', client)).resolves.toEqual({ spaceId: 'space-1', lines: [line], netResultMinor: -1250 })
    await expect(getIncomeStatement('space-1', fakeClient({ error: {} }))).rejects.toThrow('Failed to fetch income statement')
  })
})