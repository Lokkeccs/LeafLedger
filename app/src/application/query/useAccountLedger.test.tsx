// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getAccountLedger } from '../reports'
import { createQueryClient } from './queryClient'
import { useAccountLedger } from './useAccountLedger'

vi.mock('../reports', () => ({ getAccountLedger: vi.fn() }))

const mockedGetAccountLedger = vi.mocked(getAccountLedger)

function QueryProbe({ accountId }: { accountId: string | undefined }) {
  useAccountLedger('space-1', accountId, { from: '2026-01-01' })
  return null
}

describe('useAccountLedger', () => {
  beforeEach(() => mockedGetAccountLedger.mockReset())

  it('uses the account and date-scoped key and wrapper query function', async () => {
    mockedGetAccountLedger.mockResolvedValue({ spaceId: 'space-1', accountId: 'account-1', accountCode: 2000, accountName: 'Cash', accountKind: 'asset', accountCurrency: 'CHF', openingBalanceMinor: 0, closingBalanceMinor: 0, lines: [] })
    const client = createQueryClient()
    render(<QueryClientProvider client={client}><QueryProbe accountId="account-1" /></QueryClientProvider>)
    await waitFor(() => expect(mockedGetAccountLedger).toHaveBeenCalledWith('space-1', 'account-1', { from: '2026-01-01' }))
    expect(client.getQueryCache().find({ queryKey: ['reports', 'accountLedger', 'space-1', 'account-1', '2026-01-01', null] })).toBeTruthy()
  })

  it('does not query until an account is selected', () => {
    const client = createQueryClient()
    render(<QueryClientProvider client={client}><QueryProbe accountId={undefined} /></QueryClientProvider>)
    expect(mockedGetAccountLedger).not.toHaveBeenCalled()
  })
})
