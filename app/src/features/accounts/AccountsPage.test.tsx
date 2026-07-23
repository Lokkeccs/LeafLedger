// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../application/query/queryClient'
import { i18n } from '../../i18n'
import type { Account } from '../../application/accounts'
import { AccountManagementError } from '../../application/accounts'
import { AccountsPage } from './AccountsPage'

const { useAccounts, useAccountGroups, useCreateAccount, useCreateAccountGroup, useSetAccountActive, useUpdateAccount, useUpdateAccountGroup } = vi.hoisted(() => ({ useAccounts: vi.fn(), useAccountGroups: vi.fn(), useCreateAccount: vi.fn(), useCreateAccountGroup: vi.fn(), useSetAccountActive: vi.fn(), useUpdateAccount: vi.fn(), useUpdateAccountGroup: vi.fn() }))
vi.mock('../../application/query/useAccounts', () => ({ useAccounts }))
vi.mock('../../application/query/useAccountGroups', () => ({ useAccountGroups }))
vi.mock('../../application/query/useCreateAccount', () => ({ useCreateAccount }))
vi.mock('../../application/query/useCreateAccountGroup', () => ({ useCreateAccountGroup }))
vi.mock('../../application/query/useSetAccountActive', () => ({ useSetAccountActive }))
vi.mock('../../application/query/useUpdateAccount', () => ({ useUpdateAccount }))
vi.mock('../../application/query/useUpdateAccountGroup', () => ({ useUpdateAccountGroup }))

const account: Account = {
  id: 'acc-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', isActive: true,
  groupId: 'group-1', validFrom: null, validTo: null, fxPolicy: null,
}

function renderPage() {
  return render(<I18nextProvider i18n={i18n}><MemoryRouter><QueryClientProvider client={createQueryClient()}><AccountsPage /></QueryClientProvider></MemoryRouter></I18nextProvider>)
}

describe('AccountsPage', () => {
  beforeEach(() => {
    useAccounts.mockReset(); useAccountGroups.mockReset(); useCreateAccount.mockReset(); useCreateAccountGroup.mockReset(); useSetAccountActive.mockReset(); useUpdateAccount.mockReset(); useUpdateAccountGroup.mockReset()
    useAccountGroups.mockReturnValue({ isPending: false, isError: false, data: [] })
    const mutation = { isPending: false, error: null, mutate: vi.fn(), mutateAsync: vi.fn() }
    useCreateAccount.mockReturnValue(mutation); useCreateAccountGroup.mockReturnValue(mutation); useSetAccountActive.mockReturnValue(mutation); useUpdateAccount.mockReturnValue(mutation); useUpdateAccountGroup.mockReturnValue(mutation)
  })

  it('renders loading state', () => {
    useAccounts.mockReturnValue({ isPending: true })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('Loading accounts…')
  })

  it('renders an empty catalog', () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    renderPage()
    expect(screen.getAllByRole('status')[0]!.textContent).toBe('No accounts found.')
  })

  it('renders account rows through the data table', () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [account] })
    renderPage()
    expect(screen.getByRole('table', { name: 'Accounts' })).toBeTruthy()
    expect(screen.getByText('Cash')).toBeTruthy()
    expect(screen.getByText('Active')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Cash' }).getAttribute('href')).toBe('/reports/account/acc-1')
  })

  it('throws query errors for the route boundary', () => {
    const error = new Error('request failed')
    useAccounts.mockReturnValue({ isPending: false, isError: true, error })
    expect(() => renderPage()).toThrow(error)
  })

  it('renders a friendly permission message for a server 403', () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [account] })
    useCreateAccount.mockReturnValue({ isPending: false, error: new AccountManagementError(403, [{ code: 'forbidden', message: 'Forbidden' }]), mutate: vi.fn(), mutateAsync: vi.fn() })
    renderPage()
    expect(screen.getByRole('alert').textContent).toBe('You do not have permission to manage accounts in this space.')
  })
})