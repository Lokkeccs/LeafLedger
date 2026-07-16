// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { i18n } from '../i18n'
import { DataTable } from './DataTable'
import { DateField } from './DateField'
import { FormField } from './FormField'
import { FormSection } from './FormSection'
import { ModalShell } from './ModalShell'
import { MoneyInput } from './MoneyInput'
import { ToggleSwitch } from './ToggleSwitch'

function renderMoneyInput(value: number, currency: string, onChange = vi.fn()) {
  return render(<I18nextProvider i18n={i18n}><MoneyInput id="amount" label="Amount" value={value} currency={currency} onChange={onChange} /></I18nextProvider>)
}

function TwoMoneyInputs() {
  const [amounts, setAmounts] = useState({ first: 0, second: 0 })
  return <I18nextProvider i18n={i18n}><MoneyInput id="first" label="First" value={amounts.first} currency="CHF" onChange={(first) => setAmounts((current) => ({ ...current, first }))} /><MoneyInput id="second" label="Second" value={amounts.second} currency="CHF" onChange={(second) => setAmounts((current) => ({ ...current, second }))} /></I18nextProvider>
}

describe('shared UI primitives', () => {
  it('keeps DataTable empty, no-match, and row states distinct', () => {
    const columns = [{ header: 'Name', render: (row: { id: string; name: string }) => row.name }]
    const props = { columns, rowKey: (row: { id: string }) => row.id, emptyState: <p>No records</p>, noMatchState: <p>No matches</p> }
    const { rerender } = render(<DataTable {...props} data={[]} rows={[]} />)
    expect(screen.getByText('No records')).toBeTruthy()
    rerender(<DataTable {...props} data={[{ id: '1', name: 'Alpha' }]} rows={[]} />)
    expect(screen.getByText('No matches')).toBeTruthy()
    rerender(<DataTable {...props} data={[{ id: '1', name: 'Alpha' }]} rows={[{ id: '1', name: 'Alpha' }]} ariaLabel="Accounts" />)
    expect(screen.getByRole('table', { name: 'Accounts' })).toBeTruthy()
    expect(screen.getByText('Alpha')).toBeTruthy()
  })

  it('renders typed rows, actions, and row clicks in the desktop table', () => {
    const onRowClick = vi.fn()
    render(<DataTable data={[{ id: '1', name: 'Alpha' }]} rows={[{ id: '1', name: 'Alpha' }]} rowKey={(row) => row.id} emptyState="empty" noMatchState="none" onRowClick={onRowClick} columns={[{ header: 'Name', width: 120, render: (row) => row.name }, { type: 'actions', render: () => <button type="button">Open</button> }]} />)
    fireEvent.click(screen.getByText('Alpha'))
    expect(onRowClick).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: 'Open' })).toBeTruthy()
  })

  it('renders ModalShell in a portal and closes from the backdrop and button', () => {
    const onClose = vi.fn()
    render(<ModalShell title="Edit entry" titleId="edit-entry" closeLabel="Close" onClose={onClose}>Body</ModalShell>)
    const dialog = screen.getByRole('dialog', { name: 'Edit entry' })
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(screen.getByTestId('modal-body').textContent).toBe('Body')
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledOnce()
    fireEvent.mouseDown(dialog)
    fireEvent.click(dialog)
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  it('keeps form primitives controlled and labels connected', () => {
    const onToggle = vi.fn()
    const onDateChange = vi.fn()
    const { rerender } = render(<FormSection title="Details"><FormField id="reference" label="Reference" value="A-1" onChange={() => undefined} error="Required" /><FormField id="kind" label="Kind" control={<select defaultValue="asset"><option value="asset">Asset</option></select>} /><DateField id="date" label="Date" value="2026-07-14" onChange={onDateChange} /><ToggleSwitch checked={false} label="Enabled" onChange={onToggle} /></FormSection>)
    expect(screen.getByRole('textbox', { name: /^Reference/ }).getAttribute('value')).toBe('A-1')
    expect(screen.getByRole('textbox', { name: /^Reference/ }).getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByRole('combobox', { name: 'Kind' }).getAttribute('id')).toBe('kind')
    fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-07-15' } })
    fireEvent.click(screen.getByLabelText('Enabled'))
    expect(onDateChange).toHaveBeenCalledOnce()
    expect(onToggle).toHaveBeenCalledWith(true)
    rerender(<FormSection title="Details"><FormField id="reference" label="Reference" value="A-2" onChange={() => undefined} /></FormSection>)
    expect(screen.getByRole('textbox', { name: /^Reference/ }).getAttribute('value')).toBe('A-2')
  })

  it.each([
    ['USD', '12.34', 1234],
    ['USD', '-0.01', -1],
    ['JPY', '1234', 1234],
    ['BHD', '1.234', 1234],
  ])('parses %s major units to integer minor units', (currency, typed, expected) => {
    const onChange = vi.fn()
    renderMoneyInput(0, currency, onChange)
    const input = screen.getByLabelText('Amount')
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: typed } })
    fireEvent.blur(input)
    expect(onChange).toHaveBeenCalledWith(expected)
  })

  it('synchronizes valid edits before blur', () => {
    const onChange = vi.fn()
    renderMoneyInput(0, 'CHF', onChange)
    const input = screen.getByLabelText('Amount')

    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '25.00' } })

    expect(onChange).toHaveBeenCalledWith(2500)
  })

  it('preserves large safe integer amounts without float parsing', () => {
    const onChange = vi.fn()
    renderMoneyInput(9007199254740991, 'USD', onChange)
    const input = screen.getByLabelText('Amount')
    fireEvent.focus(input)
    expect((input as HTMLInputElement).value).toBe('90071992547409.91')
    fireEvent.change(input, { target: { value: '90071992547409.90' } })
    expect(onChange).toHaveBeenCalledWith(9007199254740990)
  })

  it('keeps an edited value when a sibling money field rerenders the parent', () => {
    render(<TwoMoneyInputs />)
    const first = screen.getByLabelText('First')
    const second = screen.getByLabelText('Second')

    fireEvent.focus(first)
    fireEvent.change(first, { target: { value: '25.00' } })
    fireEvent.focus(second)
    fireEvent.change(second, { target: { value: '10.00' } })

    expect((first as HTMLInputElement).value).toBe('25.00')
    expect((second as HTMLInputElement).value).toBe('10.00')
  })
})