import type { AccountInfo } from '@azure/msal-browser'

// P3-WP09 — Development/E2E-only authentication seam.
//
// Active ONLY when the app is built with `VITE_E2E_AUTH=1`. In every normal
// build the flag is unset, this module is inert, and the MSAL sign-in path is
// used unchanged. The static bearer emitted here is accepted ONLY by the Host's
// Development-gated E2E authentication scheme (`Authentication:E2E:Enabled`) and
// never in production. See `app/e2e/test-auth-contract.md` for the token/claims
// contract both ends implement.

export type E2EMember = 'a' | 'b'

const memberStorageKey = 'll.e2e.member'
const tokenPrefix = 'e2e:'

export function isE2EAuthEnabled(): boolean {
  const flag = import.meta.env.VITE_E2E_AUTH
  return flag === '1' || flag === 'true'
}

// Which seeded member this browser context authenticates as. Playwright sets
// `localStorage['ll.e2e.member']` per context (see e2e/fixtures.ts); default 'a'.
function selectedMember(): E2EMember {
  if (typeof window === 'undefined') return 'a'
  return window.localStorage.getItem(memberStorageKey) === 'b' ? 'b' : 'a'
}

export function e2eBearerToken(): string {
  return `${tokenPrefix}${selectedMember()}`
}

export function e2eAccount(): AccountInfo {
  const member = selectedMember()
  return {
    homeAccountId: `e2e-${member}`,
    localAccountId: `e2e-${member}`,
    environment: 'e2e',
    tenantId: 'e2e',
    username: `e2e-member-${member}@leafledger.test`,
    name: `E2E Member ${member.toUpperCase()}`,
  }
}
