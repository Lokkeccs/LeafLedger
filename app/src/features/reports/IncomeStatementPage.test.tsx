// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../application/query/queryClient'
import type { IncomeStatement } from '../../application/reports'
import { i18n } from '../../i18n'
import { formatMoney } from '../../i18n/format/money'
import { IncomeStatementPage } from './IncomeStatementPage'

const { useIncomeStatement } = vi.hoisted(() => ({ useIncomeStatement: vi.fn() }))
vi.mock('../../application/query/useIncomeStatement', () => ({ useIncomeStatement }))

const report: IncomeStatement = {
  spaceId: 'space-1',
  lines: [
    { accountId: 'acc-1', accountCode: 4000, name: 'Revenue', accountKind: 'Income', amountMinor: 9000, isDerived: false },
    { accountId: null, accountCode: null, name: 'Net result', accountKind: 'NetResult', amountMinor: 2500, isDerived: true },
  ],
  netResultMinor: 2500,
}

function renderPage() {
  return render(<I18nextProvider i18n={i18n}><QueryClientProvider client={createQueryClient()}><IncomeStatementPage /></QueryClientProvider></I18nextProvider>)
}

describe('IncomeStatementPage', () => {
  beforeEach(() => useIncomeStatement.mockReset())

  it('renders loading and empty states', () => {
    useIncomeStatement.mockReturnValueOnce({ isPending: true })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('Loading income statement…')
    cleanup()

    useIncomeStatement.mockReturnValueOnce({ isPending: false, isError: false, data: { ...report, lines: [] } })
    renderPage()
    expect(screen.getByRole('status').textContent).toBe('No income-statement lines found.')
  })

  it('renders income lines and the net result without changing amounts', () => {
    useIncomeStatement.mockReturnValue({ isPending: false, isError: false, data: report })
    renderPage()
    expect(screen.getByRole('heading', { name: 'Income' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Result' })).toBeTruthy()
    expect(screen.getAllByText((content) => content.replace(/\u00a0/g, ' ') === formatMoney(2500, 'CHF', i18n.language).replace(/\u00a0/g, ' '))).toHaveLength(2)
    expect(screen.getAllByText('Net result')).toHaveLength(2)
  })

  it('throws query errors for the route boundary', () => {
    const error = new Error('request failed')
    useIncomeStatement.mockReturnValue({ isPending: false, isError: true, error })
    expect(() => renderPage()).toThrow(error)
  })
})