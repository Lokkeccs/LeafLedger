// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { describe, expect, it, vi } from 'vitest'
import { AccountManagementError } from '../../application/accounts'
import type { AccountGroup } from '../../application/accountGroups'
import { i18n } from '../../i18n'
import { AccountFormModal } from './AccountFormModal'

const groups: AccountGroup[] = [{ id: 'group-1', name: 'Assets', rangeStart: 1000, rangeEnd: 1999, parentId: null, fxPolicy: null }]

function renderModal(props: Partial<React.ComponentProps<typeof AccountFormModal>> = {}) {
  return render(<I18nextProvider i18n={i18n}><AccountFormModal groups={groups} open submitting={false} onClose={vi.fn()} onSubmit={vi.fn().mockResolvedValue(undefined)} {...props} /></I18nextProvider>)
}

describe('AccountFormModal', () => {
  it('shows an explicit group placeholder and blocks incomplete creation', () => {
    const onSubmit = vi.fn()
    renderModal({ onSubmit })
    expect(screen.getByRole('option', { name: 'Select a group…' })).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: '1000' } })
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Cash' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByRole('alert').textContent).toBe('Complete the required fields.')
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submits the selected group', () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    renderModal({ onSubmit })
    fireEvent.change(screen.getByLabelText('Group'), { target: { value: 'group-1' } })
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: '1000' } })
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Cash' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ groupId: 'group-1', code: 1000, name: 'Cash' }))
  })

  it('renders server field issues while disabled', () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    renderModal({ submitting: true, error: { issues: [{ code: 'account.code_taken', message: 'Code already used', field: 'code' }] }, onSubmit })
    expect((screen.getByRole('button', { name: 'Saving…' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('Code already used')).toBeTruthy()
  })

  it('contains a rejected submission without an unhandled rejection', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new AccountManagementError(422, [{ code: 'account.invalid', message: 'Invalid account' }]))
    const unhandled = vi.fn()
    process.on('unhandledRejection', unhandled)
    renderModal({ onSubmit })
    fireEvent.change(screen.getByLabelText('Group'), { target: { value: 'group-1' } })
    fireEvent.change(screen.getByLabelText('Code'), { target: { value: '1000' } })
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Cash' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await vi.waitFor(() => expect(onSubmit).toHaveBeenCalled())
    expect(unhandled).not.toHaveBeenCalled()
    process.removeListener('unhandledRejection', unhandled)
  })
})