// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../application/query/queryClient'
import { i18n } from '../i18n'
import { createAppRouter } from './router'

const { useAccounts, useTrialBalance } = vi.hoisted(() => ({ useAccounts: vi.fn(), useTrialBalance: vi.fn() }))
vi.mock('../application/query/useAccounts', () => ({ useAccounts }))
vi.mock('../application/query/useTrialBalance', () => ({ useTrialBalance }))
vi.mock('../application/auth/useAuth', () => ({
  useAuth: () => ({ account: undefined, error: undefined, isConfigured: false, isSignedIn: false, signIn: vi.fn(), signOut: vi.fn() }),
}))

function renderRouter(initialEntry: string) {
  return render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><RouterProvider router={createAppRouter(initialEntry)} /></QueryClientProvider></I18nextProvider>)
}

describe('accounts route', () => {
  beforeEach(() => { useAccounts.mockReset(); useTrialBalance.mockReset() })

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
})