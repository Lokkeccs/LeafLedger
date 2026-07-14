import type { Configuration } from '@azure/msal-browser'

const defaultAuthority = 'https://login.microsoftonline.com/common'
const defaultApiScope = 'api://leafledger/ledger.write'

const clientId = import.meta.env.VITE_MSAL_CLIENT_ID ?? ''
const authority = import.meta.env.VITE_MSAL_AUTHORITY ?? defaultAuthority
const redirectUri = import.meta.env.VITE_MSAL_REDIRECT_URI ?? (typeof window === 'undefined' ? 'http://localhost:5173' : window.location.origin)

export const apiScope = import.meta.env.VITE_API_SCOPE ?? defaultApiScope
export const loginScopes = ['openid', 'profile', 'email', apiScope] as const

export const msalConfig: Configuration = {
  auth: { clientId, authority, redirectUri, postLogoutRedirectUri: redirectUri },
  cache: { cacheLocation: 'sessionStorage', storeAuthStateInCookie: false },
}

export function hasClientId(): boolean {
  return clientId.trim().length > 0
}