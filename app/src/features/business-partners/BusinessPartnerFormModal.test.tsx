// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { describe, expect, it, vi } from 'vitest'
import { BusinessPartnerManagementError } from '../../application/businessPartners'
import { i18n } from '../../i18n'
import { BusinessPartnerFormModal } from './BusinessPartnerFormModal'

function renderModal(props: Partial<React.ComponentProps<typeof BusinessPartnerFormModal>> = {}) {
  return render(<I18nextProvider i18n={i18n}><BusinessPartnerFormModal open submitting={false} onClose={vi.fn()} onSubmit={vi.fn().mockResolvedValue(undefined)} {...props} /></I18nextProvider>)
}

describe('BusinessPartnerFormModal', () => {
  it('blocks an empty name and invalid date order', () => {
    const onSubmit = vi.fn()
    renderModal({ onSubmit })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByRole('alert').textContent).toBe('Enter a partner name.')
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Acme' } })
    fireEvent.change(screen.getByLabelText('Valid from'), { target: { value: '2026-02-01' } })
    fireEvent.change(screen.getByLabelText('Valid to'), { target: { value: '2026-01-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByRole('alert').textContent).toBe('Valid from must be before valid to.')
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('renders a version conflict reload message', () => {
    renderModal({ error: new BusinessPartnerManagementError(409, [{ code: 'partner.version_conflict', message: 'Conflict' }]) })
    expect(screen.getByRole('alert').textContent).toBe('This partner changed elsewhere. Reload the list and try again.')
  })

  it('renders server field issues and the notes textarea while submitting', () => {
    renderModal({ submitting: true, error: new BusinessPartnerManagementError(422, [{ code: 'partner.name_taken', message: 'Name already used', field: 'name' }]) })
    expect(screen.getByRole('textbox', { name: 'Notes' }).tagName).toBe('TEXTAREA')
    expect(screen.getByText('Name already used')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Saving…' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('contains rejected server submissions', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new BusinessPartnerManagementError(422, [{ code: 'partner.invalid', message: 'Invalid partner' }]))
    const unhandled = vi.fn()
    process.on('unhandledRejection', unhandled)
    renderModal({ onSubmit })
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Acme' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await vi.waitFor(() => expect(onSubmit).toHaveBeenCalled())
    expect(unhandled).not.toHaveBeenCalled()
    process.removeListener('unhandledRejection', unhandled)
  })
})