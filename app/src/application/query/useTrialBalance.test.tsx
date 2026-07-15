// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getTrialBalance } from '../reports'
import { createQueryClient } from './queryClient'
import { useTrialBalance } from './useTrialBalance'

vi.mock('../reports', () => ({ getTrialBalance: vi.fn() }))

const mockedGetTrialBalance = vi.mocked(getTrialBalance)

function QueryProbe({ spaceId }: { spaceId: string }) {
  useTrialBalance(spaceId)
  return null
}

describe('useTrialBalance', () => {
  beforeEach(() => mockedGetTrialBalance.mockReset())

  it('uses the space-scoped report key and wrapper query function', async () => {
    mockedGetTrialBalance.mockResolvedValue({ spaceId: 'space-1', rows: [], totalBaseBalanceMinor: 0 })
    const client = createQueryClient()

    render(<QueryClientProvider client={client}><QueryProbe spaceId="space-1" /></QueryClientProvider>)

    await waitFor(() => expect(mockedGetTrialBalance).toHaveBeenCalledWith('space-1'))
    expect(client.getQueryCache().find({ queryKey: ['reports', 'trialBalance', 'space-1'] })).toBeTruthy()
  })
})