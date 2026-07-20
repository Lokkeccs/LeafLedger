// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { createQueryClient } from './queryClient'
import { useDashboardSummary } from './useDashboardSummary'

const { getDashboardSummary } = vi.hoisted(() => ({ getDashboardSummary: vi.fn() }))
vi.mock('../reports', () => ({ getDashboardSummary }))

describe('useDashboardSummary', () => {
  it('uses the dashboard query key and wrapper', async () => {
    getDashboardSummary.mockResolvedValue({ spaceId: 'space-1', totalAssetsMinor: 0, totalLiabilitiesMinor: 0, totalEquityMinor: 0, totalIncomeMinor: 0, totalExpensesMinor: 0, netResultMinor: 0, netWorthMinor: 0, accountCount: 0, balanced: true })
    const client = createQueryClient()
    const wrapper = ({ children }: { children: React.ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    renderHook(() => useDashboardSummary('space-1'), { wrapper })
    await waitFor(() => expect(getDashboardSummary).toHaveBeenCalledWith('space-1'))
    expect(client.getQueryCache().find({ queryKey: ['reports', 'dashboard', 'space-1'] })).toBeTruthy()
  })
})