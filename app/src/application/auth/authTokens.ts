import { InteractionRequiredAuthError, type AccountInfo, type AuthenticationResult } from '@azure/msal-browser'
import type { Middleware } from 'openapi-fetch'
import { apiClient } from '../../api/client'
import { e2eBearerToken, isE2EAuthEnabled } from './e2eAuth'
import { apiScope } from './msalConfig'
import { getAuthenticatedAccount, msalInstance, runMsalInteraction } from './msalInstance'

const fallbackThrottleMs = 10_000
let fallbackAttemptAt = 0
let middlewareInstalled = false

export async function acquireApiToken(account: AccountInfo | null): Promise<string | undefined> {
  if (isE2EAuthEnabled()) return e2eBearerToken()
  if (!account || !apiScope) return undefined

  try {
    const result = await msalInstance.acquireTokenSilent({ account, scopes: [apiScope] })
    return result.accessToken
  } catch (error) {
    if (!(error instanceof InteractionRequiredAuthError)) {
      throw new Error('Access token acquisition failed.', { cause: error })
    }
    const now = Date.now()
    if (now - fallbackAttemptAt < fallbackThrottleMs) {
      throw new Error('Interaction required. Please sign in again and retry.', { cause: error })
    }
    fallbackAttemptAt = now
    try {
      const result: AuthenticationResult = await runMsalInteraction(() => msalInstance.acquireTokenPopup({ scopes: [apiScope] }))
      return result.accessToken
    } catch (popupError) {
      if (popupError instanceof Error) throw popupError
      throw new Error('Interaction required. Please sign in again and retry.', { cause: popupError })
    }
  }
}

export const bearerMiddleware: Middleware = {
  onRequest: async ({ request }) => {
  const account = getAuthenticatedAccount()
  const token = await acquireApiToken(account)
  if (!token) return request
  request.headers.set('Authorization', `Bearer ${token}`)
  return request
  },
}

export function installAuthMiddleware(): void {
  if (middlewareInstalled) return
  apiClient.use(bearerMiddleware)
  middlewareInstalled = true
}