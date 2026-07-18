// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../application/query/queryClient'
import type { BalanceSheet } from '../../application/reports'
import { i18n } from '../../i18n'
import { formatMoney } from '../../i18n/format/money'
import { BalanceSheetPage } from './BalanceSheetPage'

const { useBalanceSheet } = vi.hoisted(() => ({ useBalanceSheet: vi.fn() }))
vi.mock('../../application/query/useBalanceSheet', () => ({ useBalanceSheet }))

const report: BalanceSheet = {
  spaceId: 'space-1',
  lines: [
    { accountId: 'acc-1', accountCode: 1000, name: 'Cash', accountKind: 'Asset', amountMinor: 12345, isDerived: false },
    { accountId: 'acc-2', accountCode: 2000, name: 'Payables', accountKind: 'Liability', amountMinor: -5000, isDerived: false },
    { accountId: null, accountCode: null, name: 'Current result', accountKind: 'Equity', amountMinor: -1250, isDerived: true },
  ],
  currentResultMinor: -1250,
}

function renderPage() {
  return render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><BalanceSheetPage /></QueryClientProvider></I18nextProvider>)
}

describe('BalanceSheetPage', () => {
  beforeEach(() => useBalanceSheet.mockReset())

  it('renders loading and empty states', () => {
    useBalanceSheet.mockReturnValueOnce({ isPending: true })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('Loading balance sheet…')
    cleanup()

    useBalanceSheet.mockReturnValueOnce({ isPending: false, isError: false, data: { ...report, lines: [] } })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('No balance-sheet lines found.')
  })

  it('groups lines and renders server amounts verbatim with a derived result', () => {
    useBalanceSheet.mockReturnValue({ isPending: false, isError: false, data: report })
    renderPage()

    expect(screen.getByRole('heading', { name: 'Assets' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Liabilities' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Equity' })).toBeTruthy()
    expect(screen.getAllByText((content) => content.replace(/\u00a0/g, ' ') === formatMoney(12345, 'CHF', i18n.language).replace(/\u00a0/g, ' '))).toHaveLength(1)
    expect(screen.getAllByText((content) => content.replace(/\u00a0/g, ' ') === formatMoney(-1250, 'CHF', i18n.language).replace(/\u00a0/g, ' '))).toHaveLength(2)
    expect(screen.getAllByText('Current result')).toHaveLength(2)
  })

  it('throws query errors for the route boundary', () => {
    const error = new Error('request failed')
    useBalanceSheet.mockReturnValue({ isPending: false, isError: true, error })
    expect(() => renderPage()).toThrow(error)
  })
})