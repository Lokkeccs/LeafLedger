import { afterEach, describe, expect, it, vi } from 'vitest'

describe('MSAL configuration', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('uses the common authority and session storage without a client secret', async () => {
    const { apiScope, hasClientId, msalConfig } = await import('./msalConfig')
    expect(msalConfig.auth?.authority).toBe('https://login.microsoftonline.com/common')
    expect(msalConfig.cache?.cacheLocation).toBe('sessionStorage')
    expect(msalConfig.auth?.clientId).toBe('')
    expect(hasClientId()).toBe(false)
    expect(apiScope).toBe('api://leafledger/ledger.write')
    expect(JSON.stringify(msalConfig)).not.toContain('localStorage')
  })

  it('reads client, authority, redirect, and API scope overrides from Vite env', async () => {
    vi.resetModules()
    vi.stubEnv('VITE_MSAL_CLIENT_ID', 'client-id')
    vi.stubEnv('VITE_MSAL_AUTHORITY', 'https://login.example.test/tenant')
    vi.stubEnv('VITE_MSAL_REDIRECT_URI', 'https://app.example.test/auth')
    vi.stubEnv('VITE_API_SCOPE', 'api://custom/ledger.write')

    const { apiScope, hasClientId, loginScopes, msalConfig } = await import('./msalConfig')

    expect(msalConfig.auth?.clientId).toBe('client-id')
    expect(msalConfig.auth?.authority).toBe('https://login.example.test/tenant')
    expect(msalConfig.auth?.redirectUri).toBe('https://app.example.test/auth')
    expect(msalConfig.auth?.postLogoutRedirectUri).toBe('https://app.example.test/auth')
    expect(apiScope).toBe('api://custom/ledger.write')
    expect(loginScopes).toContain('api://custom/ledger.write')
    expect(hasClientId()).toBe(true)
  })
})
