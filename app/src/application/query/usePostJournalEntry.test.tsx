// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, act } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createJournalEntrySubmission, type PostedEntry } from '../journalEntries'
import { usePostJournalEntry } from './usePostJournalEntry'

const { postJournalEntry } = vi.hoisted(() => ({ postJournalEntry: vi.fn() }))
vi.mock('../journalEntries', async () => ({ ...(await vi.importActual('../journalEntries')), postJournalEntry }))

const input = { date: '2026-07-14', description: 'Entry', reference: null, lines: [] }
const testIdempotencyKey = 'synthetic-test-idempotency-key'

describe('usePostJournalEntry', () => {
  beforeEach(() => postJournalEntry.mockReset())

  it('invalidates journal and all statement queries after posting', async () => {
    const posted: PostedEntry = { id: 'je-1', entryNo: 1, date: input.date }
    postJournalEntry.mockResolvedValue(posted)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const invalidateQueries = vi.spyOn(queryClient, 'invalidateQueries')
    const wrapper = ({ children }: { children: React.ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    const { result } = renderHook(() => usePostJournalEntry('space-1'), { wrapper })

    await act(async () => { await result.current.mutateAsync({ input, idempotencyKey: testIdempotencyKey }) })

    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['journalEntries', 'list', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'trialBalance', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'balanceSheet', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'incomeStatement', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'dashboard', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'accountLedger', 'space-1'] })
  })

  it('reuses a submission key for retries and creates a fresh key for a new submission', async () => {
    postJournalEntry.mockResolvedValue({ id: 'je-1', entryNo: 1, date: input.date })
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
    const wrapper = ({ children }: { children: React.ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    const { result } = renderHook(() => usePostJournalEntry('space-1'), { wrapper })
    const firstSubmission = createJournalEntrySubmission(input)
    const secondSubmission = createJournalEntrySubmission(input)

    await act(async () => {
      await result.current.mutateAsync(firstSubmission)
      await result.current.mutateAsync(firstSubmission)
      await result.current.mutateAsync(secondSubmission)
    })

    const keys = postJournalEntry.mock.calls.map(([, , , idempotencyKey]) => idempotencyKey)
    expect(keys[0]).toBe(keys[1])
    expect(keys[2]).not.toBe(keys[0])
    expect(firstSubmission.idempotencyKey).toBe(keys[0])
  })

})