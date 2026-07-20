// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { i18n } from '../../i18n'
import type { DashboardSummary } from '../../application/reports'
import { DashboardPage } from './DashboardPage'

const { useDashboardSummary } = vi.hoisted(() => ({ useDashboardSummary: vi.fn() }))
vi.mock('../../application/query/useDashboardSummary', () => ({ useDashboardSummary }))

const report: DashboardSummary = {
  spaceId: 'space-1', totalAssetsMinor: 123456, totalLiabilitiesMinor: 23456, totalEquityMinor: 100000,
  totalIncomeMinor: 50000, totalExpensesMinor: 12345, netResultMinor: 37655, netWorthMinor: 100000,
  accountCount: 8, balanced: true,
}

function renderPage() {
  return render(<I18nextProvider i18n={i18n}><DashboardPage /></I18nextProvider>)
}

describe('DashboardPage', () => {
  beforeEach(() => { useDashboardSummary.mockReset(); void i18n.changeLanguage('en') })

  it('renders server-provided totals with locale formatting and integrity status', () => {
    useDashboardSummary.mockReturnValue({ isPending: false, isError: false, data: report })
    renderPage()
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeTruthy()
    expect(screen.getByText(/CHF\s*1,234\.56/)).toBeTruthy()
    expect(screen.getByText(/CHF\s*376\.55/)).toBeTruthy()
    expect(screen.getByText('Ledger balanced')).toBeTruthy()
    expect(screen.getByText('8')).toBeTruthy()
  })

  it('renders the empty state and preserves an unbalanced server signal', () => {
    useDashboardSummary.mockReturnValue({ isPending: false, isError: false, data: { ...report, accountCount: 0, totalAssetsMinor: 0, totalLiabilitiesMinor: 0, totalEquityMinor: 0, totalIncomeMinor: 0, totalExpensesMinor: 0, netResultMinor: 0, netWorthMinor: 0, balanced: false } })
    renderPage()
    expect(screen.getByText('No posted activity yet. Your overview will appear here after the first entry.')).toBeTruthy()
    expect(screen.getByText('Ledger needs attention')).toBeTruthy()
  })

  it('renders loading and unbalanced states', () => {
    useDashboardSummary.mockReturnValue({ isPending: true, isError: false, data: undefined })
    renderPage()
    expect(screen.getByText('Loading dashboard…')).toBeTruthy()

    cleanup()
    useDashboardSummary.mockReturnValue({ isPending: false, isError: false, data: { ...report, balanced: false } })
    renderPage()
    expect(screen.getByText('Ledger needs attention')).toBeTruthy()
  })
})