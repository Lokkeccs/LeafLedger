// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { createQueryClient } from './queryClient'
import { useBalanceSheet } from './useBalanceSheet'

const { getBalanceSheet } = vi.hoisted(() => ({ getBalanceSheet: vi.fn() }))
vi.mock('../reports', () => ({ getBalanceSheet }))

describe('useBalanceSheet', () => {
  it('uses the balance-sheet query key and wrapper', async () => {
    getBalanceSheet.mockResolvedValue({ spaceId: 'space-1', lines: [], currentResultMinor: 0 })
    const client = createQueryClient()
    const wrapper = ({ children }: { children: React.ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    renderHook(() => useBalanceSheet('space-1'), { wrapper })
    await waitFor(() => expect(getBalanceSheet).toHaveBeenCalledWith('space-1'))
    expect(client.getQueryCache().find({ queryKey: ['reports', 'balanceSheet', 'space-1'] })).toBeTruthy()
  })
})