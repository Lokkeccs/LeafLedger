// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { describe, expect, it, vi } from 'vitest'
import { AccountManagementError } from '../../application/accounts'
import { i18n } from '../../i18n'
import { GroupFormModal } from './GroupFormModal'

function renderModal(onSubmit = vi.fn().mockResolvedValue(undefined)) {
  return { onSubmit, ...render(<I18nextProvider i18n={i18n}><GroupFormModal open submitting={false} onClose={vi.fn()} onSubmit={onSubmit} /></I18nextProvider>) }
}

describe('GroupFormModal', () => {
  it('blocks an inverted code range as UX-only validation', () => {
    const { onSubmit } = renderModal()
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Assets' } })
    fireEvent.change(screen.getByLabelText('Range start'), { target: { value: '2000' } })
    fireEvent.change(screen.getByLabelText('Range end'), { target: { value: '1000' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByRole('alert').textContent).toBe('Range start must be no greater than range end.')
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('renders a non-field server error and disables controls while submitting', () => {
    render(<I18nextProvider i18n={i18n}><GroupFormModal open submitting error={{ issues: [{ code: 'forbidden', message: 'Not allowed' }] }} onClose={vi.fn()} onSubmit={vi.fn()} /></I18nextProvider>)
    expect(screen.getByRole('alert').textContent).toBe('Not allowed')
    expect((screen.getByRole('button', { name: 'Saving…' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('contains a rejected submission without an unhandled rejection', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new AccountManagementError(422, [{ code: 'group.invalid', message: 'Invalid group' }]))
    const unhandled = vi.fn()
    process.on('unhandledRejection', unhandled)
    renderModal(onSubmit)
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Assets' } })
    fireEvent.change(screen.getByLabelText('Range start'), { target: { value: '1000' } })
    fireEvent.change(screen.getByLabelText('Range end'), { target: { value: '1999' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await vi.waitFor(() => expect(onSubmit).toHaveBeenCalled())
    expect(unhandled).not.toHaveBeenCalled()
    process.removeListener('unhandledRejection', unhandled)
  })
})