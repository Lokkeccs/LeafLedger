// @vitest-environment jsdom
import { I18nextProvider } from 'react-i18next'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { i18n } from '../../i18n'
import { AccountLedgerPage } from './AccountLedgerPage'

const { useAccounts, useAccountLedger } = vi.hoisted(() => ({ useAccounts: vi.fn(), useAccountLedger: vi.fn() }))
vi.mock('../../application/query/useAccounts', () => ({ useAccounts }))
vi.mock('../../application/query/useAccountLedger', () => ({ useAccountLedger }))

const account = { id: 'account-1', code: 2000, name: 'Cash', currency: 'CHF', kind: 'asset', isActive: true, groupId: 'group-1', validFrom: null, validTo: null, fxPolicy: null }
const expectedSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8a8a1'
const report = {
  spaceId: 'space-1', accountId: 'account-1', accountCode: 2000, accountName: 'Cash', accountKind: 'asset', accountCurrency: 'CHF', openingBalanceMinor: 100, closingBalanceMinor: 70,
  lines: [{ entryId: 'entry-1', entryNo: 1, date: '2026-01-01', description: 'Opening', reference: 'REF-1', amountMinor: 100, baseAmountMinor: 100, lineCurrency: 'CHF', runningBalanceMinor: 100 }, { entryId: 'entry-2', entryNo: 2, date: '2026-01-02', description: 'Receipt', reference: null, amountMinor: -30, baseAmountMinor: -30, lineCurrency: 'CHF', runningBalanceMinor: 70 }],
}

function renderPage(path = '/reports/account/account-1') {
  return render(<I18nextProvider i18n={i18n}><MemoryRouter initialEntries={[path]}><Routes><Route path="/reports/account/:accountId" element={<AccountLedgerPage />} /></Routes></MemoryRouter></I18nextProvider>)
}

describe('AccountLedgerPage', () => {
  beforeEach(() => {
    useAccounts.mockReturnValue({ isPending: false, isError: false, data: [account] })
    useAccountLedger.mockReturnValue({ isPending: false, isError: false, data: report })
  })

  it('renders opening, closing, posted rows, and render-edge money values', () => {
    renderPage()
    expect(screen.getByRole('heading', { name: 'Cash' })).toBeTruthy()
    expect(screen.getByText('Opening balance')).toBeTruthy()
    expect(screen.getByText('Closing balance')).toBeTruthy()
    expect(screen.getByText(/Opening · REF-1/)).toBeTruthy()
    expect(screen.getAllByText(/CHF/).length).toBeGreaterThan(0)
  })

  it('renders loading and empty states', () => {
    useAccountLedger.mockReturnValue({ isPending: true, isError: false })
    renderPage()
    expect(screen.getByText('Loading account ledger…')).toBeTruthy()

    cleanup()
    useAccountLedger.mockReturnValue({ isPending: false, isError: false, data: { ...report, lines: [] } })
    renderPage()
    expect(screen.getByText('No posted lines found for this account.')).toBeTruthy()
  })

  it('passes changed date filters to the account-ledger hook', () => {
    renderPage()
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-01-02' } })
    expect(useAccountLedger).toHaveBeenLastCalledWith(expectedSpaceId, 'account-1', { from: '2026-01-02' })
  })

  it('throws query failures to the route boundary', () => {
    useAccountLedger.mockReturnValue({ isPending: false, isError: true, error: new Error('ledger failed') })
    expect(() => renderPage()).toThrow('ledger failed')
  })
})
