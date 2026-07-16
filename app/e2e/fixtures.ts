import { test as base, type Browser, type Page } from '@playwright/test'

// Reusable two-browser fixture. `memberA` and `memberB` are independent browser
// contexts (isolated storage) authenticated as two distinct seeded members via
// the Development-only E2E test-auth seam. Playwright seeds the member selector
// into localStorage before any app script runs; the seam reads it (see
// app/src/application/auth/e2eAuth.ts and app/e2e/test-auth-contract.md).

export type E2EMember = 'a' | 'b'

const memberStorageKey = 'll.e2e.member'

async function openMemberPage(browser: Browser, member: E2EMember): Promise<Page> {
  const context = await browser.newContext()
  await context.addInitScript(
    ([key, value]) => {
      window.localStorage.setItem(key, value)
    },
    [memberStorageKey, member] as const,
  )
  return context.newPage()
}

type TwoBrowserFixtures = {
  memberA: Page
  memberB: Page
}

export const test = base.extend<TwoBrowserFixtures>({
  memberA: async ({ browser }, use) => {
    const page = await openMemberPage(browser, 'a')
    await use(page)
    await page.context().close()
  },
  memberB: async ({ browser }, use) => {
    const page = await openMemberPage(browser, 'b')
    await use(page)
    await page.context().close()
  },
})

export { expect } from '@playwright/test'
