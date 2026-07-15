import { useMsal } from '@azure/msal-react'
import { useState } from 'react'
import { queryClient } from '../query/queryClient'
import { hasClientId, loginScopes } from './msalConfig'
import { msalInstance, rememberAuthenticatedAccount, runMsalInteraction } from './msalInstance'

const popupFallbackErrorCodes = new Set(['popup_window_error', 'empty_window_error', 'monitor_window_timeout', 'block_iframes'])

export function useAuth() {
  const { accounts } = useMsal()
  const [error, setError] = useState<string | null>(null)
  const [signedOut, setSignedOut] = useState(false)
  const account = signedOut ? null : msalInstance.getActiveAccount() ?? accounts[0] ?? null

  async function signIn(): Promise<void> {
    setError(null)
    try {
      const result = await runMsalInteraction(() => msalInstance.loginPopup({ scopes: [...loginScopes] }))
      if (result.account) {
        msalInstance.setActiveAccount(result.account)
        rememberAuthenticatedAccount(result.account)
        setSignedOut(false)
      }
    } catch (signInError) {
      const errorCode = signInError instanceof Error && 'errorCode' in signInError ? signInError.errorCode : undefined
      if (typeof errorCode !== 'string' || !popupFallbackErrorCodes.has(errorCode)) {
        setError(signInError instanceof Error ? signInError.message : 'Sign-in failed. Please retry.')
        return
      }
      try {
        await runMsalInteraction(() => msalInstance.loginRedirect({ scopes: [...loginScopes] }))
      } catch (redirectError) {
        setError(redirectError instanceof Error ? redirectError.message : signInError instanceof Error ? signInError.message : 'Sign-in failed. Please retry.')
      }
    }
  }

  async function signOut(): Promise<void> {
    setError(null)
    setSignedOut(true)
    msalInstance.setActiveAccount(null)
    rememberAuthenticatedAccount(null)
    queryClient.clear()
    let signOutError: unknown
    try {
      await msalInstance.logoutPopup({ account })
    } catch (error) {
      signOutError = error
    }
    try {
      await msalInstance.clearCache(account ? { account } : undefined)
    } catch (error) {
      signOutError ??= error
    }
    if (signOutError) {
      setError(signOutError instanceof Error ? signOutError.message : 'Sign-out failed. Please retry.')
    }
  }

  return { account, error, isConfigured: hasClientId(), isSignedIn: account !== null, signIn, signOut }
}