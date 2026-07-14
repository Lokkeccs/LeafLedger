// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../application/query/queryClient'
import type { Account } from '../../application/accounts'
import { PostingError } from '../../application/journalEntries'
import { i18n } from '../../i18n'
import { JournalEntryForm } from './JournalEntryForm'

const { useAccounts, usePostJournalEntry } = vi.hoisted(() => ({ useAccounts: vi.fn(), usePostJournalEntry: vi.fn() }))
vi.mock('../../application/query/useAccounts', () => ({ useAccounts }))
vi.mock('../../application/query/usePostJournalEntry', () => ({ usePostJournalEntry }))

const accounts: Account[] = [
  { id: 'acc-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', isActive: true, groupId: 'g1', validFrom: null, validTo: null, fxPolicy: null },
  { id: 'acc-2', code: 2000, name: 'Office expense', currency: 'CHF', kind: 'Expense', isActive: true, groupId: 'g2', validFrom: null, validTo: null, fxPolicy: null },
]

function renderForm(mutate = vi.fn()) {
  useAccounts.mockReturnValue({ isPending: false, isError: false, data: accounts })
  usePostJournalEntry.mockReturnValue({ mutate, isPending: false, isSuccess: false, error: null })
  return render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><JournalEntryForm spaceId="space-1" accounts={accounts} /></QueryClientProvider></I18nextProvider>)
}

describe('JournalEntryForm', () => {
  beforeEach(() => { useAccounts.mockReset(); usePostJournalEntry.mockReset() })

  it('starts invalid and keeps posting disabled until the entry is complete', () => {
    renderForm()
    expect(screen.getByRole('button', { name: 'Post journal entry' })).toHaveProperty('disabled', true)
    expect(screen.getAllByRole('combobox')).toHaveLength(2)
  })

  it('shows the server confirmation after a successful post', () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: accounts })
    usePostJournalEntry.mockReturnValue({ mutate: vi.fn(), isPending: false, isSuccess: true, data: { entryNo: 42 }, error: null })
    render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><JournalEntryForm spaceId="space-1" accounts={accounts} /></QueryClientProvider></I18nextProvider>)
    expect(screen.getByRole('status').textContent).toContain('Journal entry 42 posted.')
  })

  it('renders line-scoped server issues', () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: accounts })
    usePostJournalEntry.mockReturnValue({ mutate: vi.fn(), isPending: false, isSuccess: false, error: new PostingError(422, [{ code: 'currency_policy.currency_not_allowed', message: 'Currency is not allowed.', line: 1 }]) })
    render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><JournalEntryForm spaceId="space-1" accounts={accounts} /></QueryClientProvider></I18nextProvider>)
    expect(screen.getByText('Line 2: Currency is not allowed.')).toBeTruthy()
  })

  it('updates the selected account through the feature-local picker', () => {
    renderForm()
    const picker = screen.getAllByRole('combobox')[0]
    if (!picker) throw new Error('account picker not rendered')
    fireEvent.change(picker, { target: { value: 'acc-1' } })
    expect((picker as HTMLSelectElement).value).toBe('acc-1')
  })

  it('adds and removes a line while preserving the two-line minimum', () => {
    renderForm()
    fireEvent.click(screen.getByRole('button', { name: 'Add line' }))
    expect(screen.getAllByRole('combobox')).toHaveLength(3)
    const removeButtons = screen.getAllByRole('button', { name: 'Remove line' })
    fireEvent.click(removeButtons[2]!)
    expect(screen.getAllByRole('combobox')).toHaveLength(2)
    expect(removeButtons[0]).toHaveProperty('disabled', true)
  })

  it('submits a balanced entry with debit-positive and credit-negative amounts', () => {
    const mutate = vi.fn()
    renderForm(mutate)
    fireEvent.change(screen.getByRole('textbox', { name: /^Description/ }), { target: { value: 'Office supplies' } })
    const pickers = screen.getAllByRole('combobox')
    fireEvent.change(pickers[0]!, { target: { value: 'acc-1' } })
    fireEvent.change(pickers[1]!, { target: { value: 'acc-2' } })
    const debitInputs = screen.getAllByLabelText('Debit')
    const creditInputs = screen.getAllByLabelText('Credit')
    fireEvent.focus(debitInputs[0]!)
    fireEvent.change(debitInputs[0]!, { target: { value: '12.50' } })
    fireEvent.blur(debitInputs[0]!)
    fireEvent.focus(creditInputs[1]!)
    fireEvent.change(creditInputs[1]!, { target: { value: '12.50' } })
    fireEvent.blur(creditInputs[1]!)
    expect(screen.getByRole('button', { name: 'Post journal entry' })).toHaveProperty('disabled', false)
    fireEvent.submit(screen.getByRole('button', { name: 'Post journal entry' }).closest('form')!)
    expect(mutate).toHaveBeenCalledWith(expect.objectContaining({ input: expect.objectContaining({ lines: expect.arrayContaining([{ accountId: 'acc-1', currency: 'CHF', amountMinor: 1250 }, { accountId: 'acc-2', currency: 'CHF', amountMinor: -1250 }]) }) }))
  })

  it('disables posting while the mutation is in flight', () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: accounts })
    usePostJournalEntry.mockReturnValue({ mutate: vi.fn(), isPending: true, isSuccess: false, error: null })
    render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><JournalEntryForm spaceId="space-1" accounts={accounts} /></QueryClientProvider></I18nextProvider>)
    expect(screen.getByRole('button', { name: 'Posting…' })).toHaveProperty('disabled', true)
  })
})