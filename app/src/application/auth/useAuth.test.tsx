// @vitest-environment jsdom
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useMsal } from '@azure/msal-react'
import { queryClient } from '../query/queryClient'
import { msalInstance } from './msalInstance'
import { useAuth } from './useAuth'

vi.hoisted(() => {
  vi.stubEnv('VITE_API_SCOPE', 'api://test/ledger.write')
})

const account = { homeAccountId: 'home', environment: 'login', tenantId: 'tenant', username: 'user@example.test', localAccountId: 'local', name: 'Test User' }

vi.mock('@azure/msal-react', () => ({ useMsal: vi.fn(), MsalProvider: ({ children }: { children: ReactNode }) => children }))
vi.mock('@azure/msal-browser', () => ({
  PublicClientApplication: class {
    acquireTokenSilent = vi.fn()
    acquireTokenPopup = vi.fn()
    clearCache = vi.fn().mockResolvedValue(undefined)
    getActiveAccount = vi.fn(() => null)
    getAllAccounts = vi.fn(() => [])
    initialize = vi.fn().mockResolvedValue(undefined)
    loginPopup = vi.fn()
    loginRedirect = vi.fn().mockResolvedValue(undefined)
    logoutPopup = vi.fn()
    setActiveAccount = vi.fn()
  },
}))

function AuthProbe() {
  const { account: currentAccount, isSignedIn, signIn, signOut, error } = useAuth()
  return <div><span>{isSignedIn ? currentAccount?.name : 'signed out'}</span><button onClick={() => void signIn()}>sign in</button><button onClick={() => void signOut()}>sign out</button>{error ? <span role="alert">{error}</span> : null}</div>
}

describe('useAuth', () => {
  beforeEach(() => {
    vi.mocked(useMsal).mockReturnValue({ accounts: [account], instance: msalInstance } as never)
    vi.mocked(msalInstance.getActiveAccount).mockReturnValue(null)
    vi.mocked(msalInstance.loginPopup).mockResolvedValue({ account } as never)
    vi.mocked(msalInstance.logoutPopup).mockResolvedValue(undefined)
    vi.mocked(msalInstance.clearCache).mockResolvedValue(undefined)
    queryClient.clear()
  })

  it('signs in through loginPopup with the configured API scope', async () => {
    render(<AuthProbe />)
    fireEvent.click(screen.getByRole('button', { name: 'sign in' }))

    await waitFor(() => expect(msalInstance.loginPopup).toHaveBeenCalledWith({ scopes: ['openid', 'profile', 'email', 'api://test/ledger.write'] }))
    expect(msalInstance.setActiveAccount).toHaveBeenCalledWith(account)
  })

  it('falls back to redirect when the host blocks the popup', async () => {
    const popupError = Object.assign(new Error('Popup blocked'), { errorCode: 'popup_window_error' })
    vi.mocked(msalInstance.loginPopup).mockRejectedValueOnce(popupError)
    render(<AuthProbe />)

    fireEvent.click(screen.getByRole('button', { name: 'sign in' }))

    await waitFor(() => expect(msalInstance.loginRedirect).toHaveBeenCalledWith({ scopes: ['openid', 'profile', 'email', 'api://test/ledger.write'] }))
  })

  it('clears the query cache and MSAL cache when logoutPopup fails', async () => {
    queryClient.setQueryData(['space', 'stale'], { name: 'old space' })
    vi.mocked(msalInstance.logoutPopup).mockRejectedValueOnce(new Error('Popup closed'))
    render(<AuthProbe />)

    fireEvent.click(screen.getByRole('button', { name: 'sign out' }))

    await waitFor(() => expect(msalInstance.clearCache).toHaveBeenCalledWith({ account }))
    expect(queryClient.getQueryData(['space', 'stale'])).toBeUndefined()
    expect(screen.getByText('signed out')).toBeTruthy()
    expect(screen.getByRole('alert').textContent).toBe('Popup closed')
  })
})
