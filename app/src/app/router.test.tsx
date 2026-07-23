// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../application/query/queryClient'
import { i18n } from '../i18n'
import { createAppRouter } from './router'

const { useAccounts, useAccountGroups, useTrialBalance, useBalanceSheet, useIncomeStatement, useAccountLedger, useDashboardSummary } = vi.hoisted(() => ({ useAccounts: vi.fn(), useAccountGroups: vi.fn(), useTrialBalance: vi.fn(), useBalanceSheet: vi.fn(), useIncomeStatement: vi.fn(), useAccountLedger: vi.fn(), useDashboardSummary: vi.fn() }))
vi.mock('../application/query/useAccounts', () => ({ useAccounts }))
vi.mock('../application/query/useAccountGroups', () => ({ useAccountGroups }))
vi.mock('../application/query/useTrialBalance', () => ({ useTrialBalance }))
vi.mock('../application/query/useBalanceSheet', () => ({ useBalanceSheet }))
vi.mock('../application/query/useIncomeStatement', () => ({ useIncomeStatement }))
vi.mock('../application/query/useAccountLedger', () => ({ useAccountLedger }))
vi.mock('../application/query/useDashboardSummary', () => ({ useDashboardSummary }))
vi.mock('../application/auth/useAuth', () => ({
  useAuth: () => ({ account: undefined, error: undefined, isConfigured: false, isSignedIn: false, signIn: vi.fn(), signOut: vi.fn() }),
}))

function renderRouter(initialEntry: string) {
  return render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><RouterProvider router={createAppRouter(initialEntry)} /></QueryClientProvider></I18nextProvider>)
}

describe('accounts route', () => {
  beforeEach(() => { useAccounts.mockReset(); useAccountGroups.mockReset(); useTrialBalance.mockReset(); useBalanceSheet.mockReset(); useIncomeStatement.mockReset(); useAccountLedger.mockReset(); useDashboardSummary.mockReset(); useAccountGroups.mockReturnValue({ isPending: false, isError: false, data: [] }) })

  it('resolves the overview route and renders the dashboard', async () => {
    useDashboardSummary.mockReturnValue({ isPending: false, isError: false, data: { spaceId: 'space-1', totalAssetsMinor: 0, totalLiabilitiesMinor: 0, totalEquityMinor: 0, totalIncomeMinor: 0, totalExpensesMinor: 0, netResultMinor: 0, netWorthMinor: 0, accountCount: 0, balanced: true } })
    renderRouter('/')
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Overview' })).toBeTruthy()
  })

  it('renders the route error boundary when the dashboard query fails', async () => {
    useDashboardSummary.mockReturnValue({ isPending: false, isError: true, error: new Error('dashboard request failed') })
    renderRouter('/')
    await waitFor(() => expect(screen.getByRole('heading', { name: 'We could not open this view' })).toBeTruthy())
    expect(screen.queryByText('dashboard request failed')).toBeNull()
  })

  it('resolves the lazy accounts route and renders the page', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    renderRouter('/accounts')
    expect(await screen.findByRole('heading', { name: 'Accounts' })).toBeTruthy()
  })

  it('renders the route error boundary when the accounts query fails', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: true, error: new Error('request failed') })
    renderRouter('/accounts')
    await waitFor(() => expect(screen.getByRole('heading', { name: 'We could not open this view' })).toBeTruthy())
    expect(screen.queryByText('request failed')).toBeNull()
  })

  it('resolves the lazy new journal-entry route under the app shell', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    renderRouter('/journal-entries/new')
    expect(await screen.findByRole('heading', { name: 'New journal entry' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'New journal entry' })).toBeTruthy()
  })

  it('renders the route error boundary when journal-entry accounts fail', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: true, error: new Error('accounts request failed') })
    renderRouter('/journal-entries/new')
    await waitFor(() => expect(screen.getByRole('heading', { name: 'We could not open this view' })).toBeTruthy())
    expect(screen.queryByText('accounts request failed')).toBeNull()
  })

  it('resolves the lazy trial-balance route under the app shell', async () => {
    useTrialBalance.mockReturnValue({ isPending: false, isError: false, data: { spaceId: 'space-1', rows: [], totalBaseBalanceMinor: 0 } })
    renderRouter('/reports/trial-balance')
    expect(await screen.findByRole('heading', { name: 'Trial balance' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Trial balance' })).toBeTruthy()
  })

  it('resolves both core-statement routes under the app shell', async () => {
    useBalanceSheet.mockReturnValue({ isPending: false, isError: false, data: { spaceId: 'space-1', lines: [], currentResultMinor: 0 } })
    renderRouter('/reports/balance-sheet')
    expect(await screen.findByRole('heading', { name: 'Balance sheet' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Balance sheet' })).toBeTruthy()

    cleanup()
    useIncomeStatement.mockReturnValue({ isPending: false, isError: false, data: { spaceId: 'space-1', lines: [], netResultMinor: 0 } })
    renderRouter('/reports/income-statement')
    expect(await screen.findByRole('heading', { name: 'Income statement' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Income statement' })).toBeTruthy()
  })

  it('renders the route error boundary when the balance-sheet query fails', async () => {
    useBalanceSheet.mockReturnValue({ isPending: false, isError: true, error: new Error('balance-sheet request failed') })
    renderRouter('/reports/balance-sheet')
    await waitFor(() => expect(screen.getByRole('heading', { name: 'We could not open this view' })).toBeTruthy())
    expect(screen.queryByText('balance-sheet request failed')).toBeNull()
  })

  it('renders the route error boundary when the income-statement query fails', async () => {
    useIncomeStatement.mockReturnValue({ isPending: false, isError: true, error: new Error('income-statement request failed') })
    renderRouter('/reports/income-statement')
    await waitFor(() => expect(screen.getByRole('heading', { name: 'We could not open this view' })).toBeTruthy())
    expect(screen.queryByText('income-statement request failed')).toBeNull()
  })

  it('resolves the lazy design-system route under the app shell', async () => {
    renderRouter('/design')
    expect(await screen.findByRole('heading', { name: 'Design system' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Shared primitives' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Open modal' })).toBeTruthy()
  })

  it('renders the route error boundary when the trial-balance query fails', async () => {
    useTrialBalance.mockReturnValue({ isPending: false, isError: true, error: new Error('report request failed') })
    renderRouter('/reports/trial-balance')
    await waitFor(() => expect(screen.getByRole('heading', { name: 'We could not open this view' })).toBeTruthy())
    expect(screen.queryByText('report request failed')).toBeNull()
  })

  it('resolves the account-ledger route under the app shell', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    useAccountLedger.mockReturnValue({ isPending: false, isError: false, data: { spaceId: 'space-1', accountId: 'account-1', accountCode: 2000, accountName: 'Cash', accountKind: 'asset', accountCurrency: 'CHF', openingBalanceMinor: 0, closingBalanceMinor: 0, lines: [] } })
    renderRouter('/reports/account/account-1')
    expect(await screen.findByRole('heading', { name: 'Cash' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Account ledger' })).toBeTruthy()
  })

  it('resolves the selector-only account-ledger route without an account query', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    useAccountLedger.mockReturnValue({ isPending: false, isError: false, data: undefined })
    renderRouter('/reports/account')
    expect(await screen.findByRole('heading', { name: 'Account ledger' })).toBeTruthy()
    expect(useAccountLedger).toHaveBeenCalledWith(expect.any(String), undefined, {})
  })

  it('navigates from the shell account-ledger link to the selector route', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    useAccountLedger.mockReturnValue({ isPending: false, isError: false, data: undefined })
    renderRouter('/accounts')
    fireEvent.click(await screen.findByRole('link', { name: 'Account ledger' }))
    expect(await screen.findByRole('heading', { name: 'Account ledger' })).toBeTruthy()
  })

  it('renders the route error boundary when the account-ledger query fails', async () => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [] })
    useAccountLedger.mockReturnValue({ isPending: false, isError: true, error: new Error('ledger request failed') })
    renderRouter('/reports/account/account-1')
    await waitFor(() => expect(screen.getByRole('heading', { name: 'We could not open this view' })).toBeTruthy())
    expect(screen.queryByText('ledger request failed')).toBeNull()
  })
})