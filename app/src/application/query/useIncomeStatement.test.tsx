// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { createQueryClient } from './queryClient'
import { useIncomeStatement } from './useIncomeStatement'

const { getIncomeStatement } = vi.hoisted(() => ({ getIncomeStatement: vi.fn() }))
vi.mock('../reports', () => ({ getIncomeStatement }))

describe('useIncomeStatement', () => {
  it('uses the income-statement query key and wrapper', async () => {
    getIncomeStatement.mockResolvedValue({ spaceId: 'space-1', lines: [], netResultMinor: 0 })
    const client = createQueryClient()
    const wrapper = ({ children }: { children: React.ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    renderHook(() => useIncomeStatement('space-1'), { wrapper })
    await waitFor(() => expect(getIncomeStatement).toHaveBeenCalledWith('space-1'))
    expect(client.getQueryCache().find({ queryKey: ['reports', 'incomeStatement', 'space-1'] })).toBeTruthy()
  })
})