import { afterEach, describe, expect, it, vi } from 'vitest'
import { e2eAccount, e2eBearerToken, isE2EAuthEnabled } from './e2eAuth'

afterEach(() => {
  vi.unstubAllEnvs()
})

describe('e2eAuth seam', () => {
  it('is inert when VITE_E2E_AUTH is unset', () => {
    vi.stubEnv('VITE_E2E_AUTH', '')
    expect(isE2EAuthEnabled()).toBe(false)
  })

  it('activates for "1" and "true"', () => {
    vi.stubEnv('VITE_E2E_AUTH', '1')
    expect(isE2EAuthEnabled()).toBe(true)
    vi.stubEnv('VITE_E2E_AUTH', 'true')
    expect(isE2EAuthEnabled()).toBe(true)
  })

  it('emits a prefixed bearer for the default member', () => {
    // node test env has no window, so selectedMember() defaults to member 'a'
    expect(e2eBearerToken()).toBe('e2e:a')
  })

  it('builds a synthetic account for the default member', () => {
    const account = e2eAccount()
    expect(account).toMatchObject({
      homeAccountId: 'e2e-a',
      localAccountId: 'e2e-a',
      environment: 'e2e',
      tenantId: 'e2e',
      username: 'e2e-member-a@leafledger.test',
      name: 'E2E Member A',
    })
  })
})
