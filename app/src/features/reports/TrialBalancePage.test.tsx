// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { cleanup, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../application/query/queryClient'
import type { TrialBalance } from '../../application/reports'
import { i18n } from '../../i18n'
import { formatMoney } from '../../i18n/format/money'
import { TrialBalancePage } from './TrialBalancePage'

const { useTrialBalance } = vi.hoisted(() => ({ useTrialBalance: vi.fn() }))
vi.mock('../../application/query/useTrialBalance', () => ({ useTrialBalance }))

const report: TrialBalance = {
  spaceId: 'space-1',
  rows: [
    { accountId: 'acc-1', accountCode: 1000, accountName: 'Cash', accountKind: 'Asset', baseBalanceMinor: 12345 },
    { accountId: 'acc-2', accountCode: 2000, accountName: 'Payables', accountKind: 'Liability', baseBalanceMinor: -12345 },
    { accountId: 'acc-3', accountCode: 3000, accountName: 'Equity', accountKind: 'Equity', baseBalanceMinor: 0 },
  ],
  totalBaseBalanceMinor: 0,
}

function renderPage() {
  return render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><TrialBalancePage /></QueryClientProvider></I18nextProvider>)
}

describe('TrialBalancePage', () => {
  beforeEach(() => useTrialBalance.mockReset())

  it('renders loading and empty states', () => {
    useTrialBalance.mockReturnValueOnce({ isPending: true })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('Loading trial balance…')
    cleanup()

    useTrialBalance.mockReturnValueOnce({ isPending: false, isError: false, data: { ...report, rows: [] } })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('No posted balances found.')
  })

  it('splits signed balances into debit and credit and shows balanced totals', () => {
    useTrialBalance.mockReturnValue({ isPending: false, isError: false, data: report })
    renderPage()

    expect(screen.getByRole('table', { name: 'Trial balance' })).toBeTruthy()
    expect(screen.getAllByText((content) => content.replace(/\u00a0/g, ' ') === formatMoney(12345, 'CHF', i18n.language).replace(/\u00a0/g, ' '))).toHaveLength(4)
    expect(screen.getAllByText('-')).toHaveLength(4)
    expect(screen.getByText('Balanced')).toBeTruthy()
    expect(screen.getByText('Total debit')).toBeTruthy()
    expect(screen.getByText('Total credit')).toBeTruthy()
  })

  it('shows an unbalanced status from the server total', () => {
    useTrialBalance.mockReturnValue({ isPending: false, isError: false, data: { ...report, totalBaseBalanceMinor: 1 } })
    renderPage()
    expect(screen.getByText('Unbalanced')).toBeTruthy()
  })

  it('throws query errors for the route boundary', () => {
    const error = new Error('request failed')
    useTrialBalance.mockReturnValue({ isPending: false, isError: true, error })
    expect(() => renderPage()).toThrow(error)
  })
})