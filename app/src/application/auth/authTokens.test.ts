// @vitest-environment jsdom
import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getMeta } from '../meta'
import { acquireApiToken, bearerMiddleware, installAuthMiddleware } from './authTokens'
import { msalInstance } from './msalInstance'

const fetchMock = vi.hoisted(() => {
  const mock = vi.fn()
  const NativeRequest = globalThis.Request
  vi.stubGlobal('Request', class extends NativeRequest {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === 'string' ? new URL(input, 'http://localhost') : input, init)
    }
  })
  vi.stubGlobal('fetch', mock)
  return mock
})

vi.hoisted(() => {
  vi.stubEnv('VITE_API_SCOPE', 'api://test/ledger.write')
})

const account = { homeAccountId: 'home', environment: 'login', tenantId: 'tenant', username: 'user@example.test', localAccountId: 'local' }

vi.mock('@azure/msal-browser', async () => {
  const actual = await vi.importActual<typeof import('@azure/msal-browser')>('@azure/msal-browser')
  return { ...actual, PublicClientApplication: class {
    acquireTokenSilent = vi.fn()
    acquireTokenPopup = vi.fn()
    getActiveAccount = vi.fn()
    getAllAccounts = vi.fn(() => [])
    initialize = vi.fn().mockResolvedValue(undefined)
    setActiveAccount = vi.fn()
  } }
})

describe('API bearer middleware', () => {
  beforeEach(() => {
    vi.mocked(msalInstance.getActiveAccount).mockReturnValue(account)
    vi.mocked(msalInstance.getAllAccounts).mockReturnValue([])
    vi.mocked(msalInstance.acquireTokenSilent).mockResolvedValue({ accessToken: 'token' } as never)
  })

  it('attaches a silent access token to generated-client requests', async () => {
    const request = new Request('http://localhost/api/v1/meta')
    await bearerMiddleware.onRequest?.({ request, options: {}, id: 'test' } as never)

    expect(request.headers.get('Authorization')).toBe('Bearer token')
  })

  it('leaves anonymous requests without an authorization header', async () => {
    vi.mocked(msalInstance.getActiveAccount).mockReturnValue(null)
    vi.mocked(msalInstance.getAllAccounts).mockReturnValue([])
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ name: 'LeafLedger', version: 'v1' }), {
      headers: { 'Content-Type': 'application/json' },
    }))
    installAuthMiddleware()

    await expect(getMeta()).resolves.toEqual({ name: 'LeafLedger', version: 'v1' })
    const request = fetchMock.mock.calls[0]?.[0] as Request
    expect(request.headers.get('Authorization')).toBeNull()
  })

  it('uses one popup fallback and throttles a second attempt', async () => {
    const silent = vi.mocked(msalInstance.acquireTokenSilent)
    const popup = vi.mocked(msalInstance.acquireTokenPopup)
    silent.mockRejectedValue(new InteractionRequiredAuthError())
    popup.mockResolvedValueOnce({ accessToken: 'popup-token' } as never)

    await expect(acquireApiToken(account)).resolves.toBe('popup-token')
    await expect(acquireApiToken(account)).rejects.toThrow('Interaction required')
    expect(popup).toHaveBeenCalledOnce()
  })
})
