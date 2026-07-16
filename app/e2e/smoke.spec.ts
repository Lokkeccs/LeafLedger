import { expect, test } from './fixtures'

// P3-WP09 @smoke — authenticated render health check.
//
// Confirms the core Phase-3 pages render for an authenticated member behind the
// E2E test-auth seam (no MSAL interaction), i.e. the seam wires a real identity
// through to authorized data access on every primary route.

test.describe('authenticated render smoke @smoke', () => {
  test('home renders', async ({ memberA }) => {
    await memberA.goto('/')
    await expect(memberA.getByRole('heading', { level: 1 })).toBeVisible()
  })

  test('accounts renders a table', async ({ memberA }) => {
    await memberA.goto('/accounts')
    await expect(memberA.getByRole('table')).toBeVisible()
  })

  test('trial balance renders its seeded empty state', async ({ memberA }) => {
    await memberA.goto('/reports/trial-balance')
    await expect(memberA.getByRole('heading', { name: /trial balance/i })).toBeVisible()
    await expect(memberA.getByRole('status')).toBeVisible()
  })

  test('new journal entry renders the line editor', async ({ memberA }) => {
    await memberA.goto('/journal-entries/new')
    await expect(memberA.locator('#journal-line-0-account')).toBeVisible()
  })
})
