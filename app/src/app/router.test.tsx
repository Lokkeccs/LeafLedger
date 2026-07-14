// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../application/query/queryClient'
import { i18n } from '../i18n'
import { createAppRouter } from './router'

const { useAccounts } = vi.hoisted(() => ({ useAccounts: vi.fn() }))
vi.mock('../application/query/useAccounts', () => ({ useAccounts }))
vi.mock('../application/auth/useAuth', () => ({
  useAuth: () => ({ account: undefined, error: undefined, isConfigured: false, isSignedIn: false, signIn: vi.fn(), signOut: vi.fn() }),
}))

function renderRouter(initialEntry: string) {
  return render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><RouterProvider router={createAppRouter(initialEntry)} /></QueryClientProvider></I18nextProvider>)
}

describe('accounts route', () => {
  beforeEach(() => useAccounts.mockReset())

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
})