import { expect, test } from './fixtures'

// P3-WP09 @smoke — Phase-3 exit criterion, browser level.
//
// Two authenticated members view the same space. Member A posts a balanced
// journal entry. Member B, already on the trial-balance page, must see the
// totals update live — driven by the SignalR space-invalidation ping — WITHOUT
// reloading the page. Member B's page is never navigated or reloaded after the
// baseline is captured, so a changed total proves the live-update path end to end.

const trialBalancePath = '/reports/trial-balance'
const newJournalEntryPath = '/journal-entries/new'

// Seeded demo accounts (DevSeed): option text is `${code} — ${name}`.
const officeExpensesOption = '6000 — Office expenses'
const bankOption = '1020 — Bank'

function debitTotal(page: import('@playwright/test').Page) {
  return page
    .locator('.report-summary > div')
    .filter({ hasText: 'Total debit' })
    .locator('dd')
}

async function debitTotalText(page: import('@playwright/test').Page): Promise<string | null> {
  const dd = debitTotal(page)
  if ((await dd.count()) === 0) return null
  return (await dd.first().textContent())?.trim() ?? null
}

test('a committed post live-updates a second browser without reload @smoke', async ({ memberA, memberB }) => {
  const reportRequestStartTimes: number[] = []
  let postCompletedAt = 0
  let mainFrameNavigations = 0
  memberB.on('request', (request) => {
    if (request.method() === 'GET' && request.url().includes('/reports/trial-balance')) {
      reportRequestStartTimes.push(Date.now())
    }
  })
  memberB.on('framenavigated', (frame) => {
    if (frame === memberB.mainFrame()) mainFrameNavigations += 1
  })

  // Member B watches the trial balance and joins the space invalidation group.
  const signalRReady = memberB
    .waitForEvent('websocket', { predicate: (socket) => socket.url().includes('/hubs/space') })
    .then((socket) => socket.waitForEvent('framereceived'))
  await memberB.goto(trialBalancePath)
  await expect(memberB.getByRole('heading', { name: /trial balance/i })).toBeVisible()
  await signalRReady
  const initialDebitTotal = await debitTotalText(memberB)
  const initialNavigationCount = mainFrameNavigations

  // Member A posts a balanced entry: debit Office expenses / credit Bank.
  await memberA.goto(newJournalEntryPath)
  await expect(memberA.locator('#journal-line-0-account')).toBeVisible()

  await memberA.locator('#journal-description').fill('P3-WP09 live-update smoke')

  await memberA.locator('#journal-line-0-account').selectOption({ label: officeExpensesOption })
  await memberA.locator('#journal-line-1-account').selectOption({ label: bankOption })

  await memberA.locator('#journal-line-1-credit').click()
  await memberA.locator('#journal-line-1-credit').press('Control+A')
  await memberA.locator('#journal-line-1-credit').pressSequentially('25.00')
  await memberA.locator('#journal-line-1-credit').press('Tab')
  await expect(memberA.locator('#journal-line-1-credit')).toHaveValue(/25/)

  await memberA.locator('#journal-line-0-debit').click()
  await memberA.locator('#journal-line-0-debit').press('Control+A')
  await memberA.locator('#journal-line-0-debit').pressSequentially('25.00')
  await memberA.locator('#journal-line-0-debit').press('Tab')
  await expect(memberA.locator('#journal-line-0-debit')).toHaveValue(/25/)

  await memberA.getByRole('button', { name: /post journal entry/i }).click()
  await expect(memberA.getByText(/posted/i)).toBeVisible()
  postCompletedAt = Date.now()

  // Member B must refetch the report and update in place with no reload/navigation.
  await expect
    .poll(() => reportRequestStartTimes.some((startedAt) => startedAt > postCompletedAt), {
      timeout: 15_000,
      message: 'browser B did not refetch the trial balance after browser A posted',
    })
    .toBe(true)
  await expect
    .poll(async () => debitTotalText(memberB), {
      timeout: 15_000,
      message: 'trial balance did not live-update in the second browser',
    })
    .not.toBe(initialDebitTotal)
  expect(mainFrameNavigations).toBe(initialNavigationCount)
})
