// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createAccount, createAccountGroup, getAccountGroups, setAccountActive, updateAccount, updateAccountGroup } from '../accounts'
import { useAccountGroups } from './useAccountGroups'
import { useCreateAccount } from './useCreateAccount'
import { useCreateAccountGroup } from './useCreateAccountGroup'
import { useSetAccountActive } from './useSetAccountActive'
import { useUpdateAccount } from './useUpdateAccount'
import { useUpdateAccountGroup } from './useUpdateAccountGroup'

vi.mock('../accounts', () => ({
  createAccount: vi.fn().mockResolvedValue({}), createAccountGroup: vi.fn().mockResolvedValue({}), getAccountGroups: vi.fn().mockResolvedValue([]),
  setAccountActive: vi.fn().mockResolvedValue({}), updateAccount: vi.fn().mockResolvedValue({}), updateAccountGroup: vi.fn().mockResolvedValue({}),
}))

const mutationInput = { input: {}, idempotencyKey: '01HHOOK' }

function createWrapper(client: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) { return <QueryClientProvider client={client}>{children}</QueryClientProvider> }
}

describe('account management query hooks', () => {
  beforeEach(() => vi.clearAllMocks())

  it('queries groups with the space-scoped key', async () => {
    const client = new QueryClient()
    renderHook(() => useAccountGroups('space-1'), { wrapper: createWrapper(client) })
    await vi.waitFor(() => expect(getAccountGroups).toHaveBeenCalledWith('space-1'))
    expect(client.getQueryCache().find({ queryKey: ['accountGroups', 'list', 'space-1'] })).toBeTruthy()
  })

  it('invalidates account and report queries after account mutations', async () => {
    const client = new QueryClient()
    const invalidate = vi.spyOn(client, 'invalidateQueries').mockResolvedValue()
    const wrapper = createWrapper(client)
    const create = renderHook(() => useCreateAccount('space-1'), { wrapper })
    await act(async () => { await create.result.current.mutateAsync(mutationInput as never) })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['accounts', 'list', 'space-1'] })
    const update = renderHook(() => useUpdateAccount('space-1'), { wrapper })
    await act(async () => { await update.result.current.mutateAsync({ accountId: 'acc-1', submission: mutationInput } as never) })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['reports', 'trialBalance', 'space-1'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['reports', 'balanceSheet', 'space-1'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['reports', 'incomeStatement', 'space-1'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['reports', 'dashboard', 'space-1'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['reports', 'accountLedger', 'space-1'] })
    const active = renderHook(() => useSetAccountActive('space-1'), { wrapper })
    await act(async () => { await active.result.current.mutateAsync({ accountId: 'acc-1', active: false }) })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['reports', 'trialBalance', 'space-1'] })
  })

  it('invalidates account and group queries after group mutations', async () => {
    const client = new QueryClient()
    const invalidate = vi.spyOn(client, 'invalidateQueries').mockResolvedValue()
    const wrapper = createWrapper(client)
    const create = renderHook(() => useCreateAccountGroup('space-1'), { wrapper })
    await act(async () => { await create.result.current.mutateAsync(mutationInput as never) })
    const update = renderHook(() => useUpdateAccountGroup('space-1'), { wrapper })
    await act(async () => { await update.result.current.mutateAsync({ groupId: 'group-1', submission: mutationInput } as never) })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['accountGroups', 'list', 'space-1'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['accounts', 'list', 'space-1'] })
    expect(createAccount).not.toHaveBeenCalled()
    expect(createAccountGroup).toHaveBeenCalled()
    expect(setAccountActive).toHaveBeenCalledTimes(0)
    expect(updateAccount).toHaveBeenCalledTimes(0)
    expect(updateAccountGroup).toHaveBeenCalled()
  })
})