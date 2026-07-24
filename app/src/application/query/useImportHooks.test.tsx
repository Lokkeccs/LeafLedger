// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useImportAccounts } from './useImportAccounts'
import { useImportGroups } from './useImportGroups'

const { importAccountsCsv, importGroupsCsv } = vi.hoisted(() => ({ importAccountsCsv: vi.fn(), importGroupsCsv: vi.fn() }))
vi.mock('../accountImport', () => ({ importAccountsCsv, importGroupsCsv }))

function createWrapper(client: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
}

describe('account import mutation hooks', () => {
  beforeEach(() => {
    importAccountsCsv.mockReset().mockResolvedValue({ total: 0, created: 0, updated: 0, failed: 0, rows: [] })
    importGroupsCsv.mockReset().mockResolvedValue({ total: 0, created: 0, updated: 0, failed: 0, rows: [] })
  })

  it('invalidates account, group, and report queries after account import', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const invalidateQueries = vi.spyOn(client, 'invalidateQueries').mockResolvedValue()
    const hook = renderHook(() => useImportAccounts('space-1'), { wrapper: createWrapper(client) })

    await act(async () => { await hook.result.current.mutateAsync('accounts-csv') })

    expect(importAccountsCsv).toHaveBeenCalledWith('space-1', 'accounts-csv')
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['accounts', 'list', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['accountGroups', 'list', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'trialBalance', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'accountLedger', 'space-1'] })
  })

  it('invalidates account, group, and report queries after group import', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const invalidateQueries = vi.spyOn(client, 'invalidateQueries').mockResolvedValue()
    const hook = renderHook(() => useImportGroups('space-1'), { wrapper: createWrapper(client) })

    await act(async () => { await hook.result.current.mutateAsync('groups-csv') })

    expect(importGroupsCsv).toHaveBeenCalledWith('space-1', 'groups-csv')
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['accounts', 'list', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['accountGroups', 'list', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'dashboard', 'space-1'] })
  })
})