// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../application/query/queryClient'
import { i18n } from '../../i18n'
import type { Account } from '../../application/accounts'
import { AccountsPage } from './AccountsPage'

const { useAccounts } = vi.hoisted(() => ({ useAccounts: vi.fn() }))
vi.mock('../../application/query/useAccounts', () => ({ useAccounts }))

const account: Account = {
  id: 'acc-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', isActive: true,
  groupId: 'group-1', validFrom: null, validTo: null, fxPolicy: null,
}

function renderPage() {
  return render(<I18nextProvider i18n={i18n}><MemoryRouter><QueryClientProvider client={createQueryClient()}><AccountsPage /></QueryClientProvider></MemoryRouter></I18nextProvider>)
}

describe('AccountsPage', () => {
  beforeEach(() => useAccounts.mockReset())

  it('renders loading state', () => {
    useAccounts.mockReturnValue({ isPending: true })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('Loading accounts…')
  })

  it('renders an empty catalog', () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('No accounts found.')
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
})