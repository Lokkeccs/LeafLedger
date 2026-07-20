import { describe, expect, it, vi } from 'vitest'
import type { ApiClient } from '../api/client'
import { getAccountLedger, getBalanceSheet, getDashboardSummary, getIncomeStatement, getTrialBalance } from './reports'

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

describe('getAccountLedger', () => {
  const line = { entryId: 'entry-1', entryNo: 4, date: '2026-01-02', description: 'Receipt', reference: 'REF-4', amountMinor: -30, baseAmountMinor: -30, lineCurrency: 'CHF', runningBalanceMinor: 70 }

  it('maps the account-ledger response and passes the date range', async () => {
    const client = fakeClient({ data: { spaceId: 'space-1', accountId: 'account-1', accountCode: 2000, accountName: 'Cash', accountKind: 'asset', accountCurrency: 'CHF', openingBalanceMinor: 100, closingBalanceMinor: 70, lines: [line] } })
    await expect(getAccountLedger('space-1', 'account-1', { from: '2026-01-02', to: '2026-01-31' }, client)).resolves.toEqual({ spaceId: 'space-1', accountId: 'account-1', accountCode: 2000, accountName: 'Cash', accountKind: 'asset', accountCurrency: 'CHF', openingBalanceMinor: 100, closingBalanceMinor: 70, lines: [line] })
    expect(client.GET).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/reports/account-ledger/{accountId}', { params: { path: { spaceId: 'space-1', accountId: 'account-1' }, query: { from: '2026-01-02', to: '2026-01-31' } } })
  })

  it('throws when the generated client returns no data', async () => {
    await expect(getAccountLedger('space-1', 'account-1', {}, fakeClient({ error: {} }))).rejects.toThrow('Failed to fetch account ledger')
  })
})

describe('getDashboardSummary', () => {
  it('maps the server-computed summary without changing minor units', async () => {
    const summary = { spaceId: 'space-1', totalAssetsMinor: 100, totalLiabilitiesMinor: 40, totalEquityMinor: 10, totalIncomeMinor: 80, totalExpensesMinor: 30, netResultMinor: 50, netWorthMinor: 60, accountCount: 5, balanced: true }
    const client = fakeClient({ data: summary })

    await expect(getDashboardSummary('space-1', client)).resolves.toEqual(summary)
    expect(client.GET).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/reports/dashboard', { params: { path: { spaceId: 'space-1' } } })
  })

  it('throws when the generated client returns no data', async () => {
    await expect(getDashboardSummary('space-1', fakeClient({ error: {} }))).rejects.toThrow('Failed to fetch dashboard summary')
  })
})