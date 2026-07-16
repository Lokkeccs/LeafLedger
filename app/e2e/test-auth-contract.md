# E2E test-auth seam — contract

Development-only seam that lets headless Playwright authenticate without interactive
MSAL/Entra sign-in. It is **fail-closed**: inert unless explicitly enabled on both ends,
and it never accepts these tokens in Production.

## Double gate (both must hold)

| Side | Gate | Effect |
| --- | --- | --- |
| Frontend | build-time `VITE_E2E_AUTH=1` | Swaps MSAL for a static bearer + synthetic account (`app/src/application/auth/e2eAuth.ts`). Unset ⇒ MSAL path byte-unchanged. |
| Host | `ASPNETCORE_ENVIRONMENT=Development` **and** `Authentication:E2E:Enabled=true` | Registers the E2E authentication scheme. Missing either ⇒ scheme not registered; bearer rejected. |

## Token format

```
Authorization: Bearer e2e:<member>
```

`<member>` ∈ `{ a, b }`. The browser context selects the member via
`localStorage['ll.e2e.member']` (default `a`), seeded by the Playwright fixture
(`app/e2e/fixtures.ts`) before any app script runs.

## Claims mapping (Host responsibility)

The Host E2E scheme maps each token to a **seeded** identity and then flows through the
exact real path (`resolve_identity_link` → membership → RLS). No membership is minted at
request time; DevSeed must have provisioned it.

| Token | Subject (oid) | Tenant (tid) | Seeded as |
| --- | --- | --- | --- |
| `e2e:a` | `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa` | `11111111-1111-1111-1111-111111111111` | Demo-space member A |
| `e2e:b` | `bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb` | `11111111-1111-1111-1111-111111111111` | Demo-space member B |

Both members belong to the seeded demo space
`8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1` (DevSeed `DefaultSpaceId`).

## Backend obligations (routed to LL Backend Dev)

The frontend half of this seam is implemented in this WP. The Playwright journey only
runs green once the backend half exists:

1. `E2ETestAuthenticationHandler` registered only when Development **and**
   `Authentication:E2E:Enabled`, parsing `e2e:<member>` → subject/tenant above.
2. `DevSeed` provisions both members' demo-space memberships (idempotent, Development-only).
3. `docker-compose.e2e.yml` runs the Host with the gate enabled and seeding on.
4. `main.yml` `e2e-smoke` job (main-blocking) brings up the stack and runs `npm run e2e`.
