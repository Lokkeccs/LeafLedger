// @vitest-environment jsdom
import { fireEvent, render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { describe, expect, it, vi } from 'vitest'
import { i18n } from '../../i18n'
import { AccountManagementError } from '../../application/accounts'
import type { ImportReport } from '../../application/accountImport'
import { ImportModal } from './ImportModal'

function renderModal(props: Partial<React.ComponentProps<typeof ImportModal>> = {}) {
  const onSubmit = props.onSubmit ?? vi.fn().mockResolvedValue(undefined)
  return { onSubmit, ...render(<I18nextProvider i18n={i18n}><ImportModal open submitting={false} onClose={vi.fn()} onSubmit={onSubmit} {...props} /></I18nextProvider>) }
}

describe('ImportModal', () => {
  it('requires a CSV file before submitting', () => {
    const { onSubmit } = renderModal()
    fireEvent.click(screen.getByRole('button', { name: 'Import' }))
    expect(screen.getByRole('alert').textContent).toBe('Choose a CSV file.')
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('passes the selected catalog type and CSV contents to the server mutation', async () => {
    const { onSubmit } = renderModal()
    const file = new File(['name,rangeStart,rangeEnd\nAssets,1000,1999'], 'groups.csv', { type: 'text/csv' })
    Object.defineProperty(file, 'text', { value: vi.fn().mockResolvedValue('name,rangeStart,rangeEnd\nAssets,1000,1999') })
    fireEvent.change(screen.getByLabelText('CSV file'), { target: { files: [file] } })
    await vi.waitFor(() => expect(screen.getByText('groups.csv')).toBeTruthy())
    fireEvent.change(screen.getByLabelText('Catalog type'), { target: { value: 'groups' } })
    fireEvent.click(screen.getByRole('button', { name: 'Import' }))
    await vi.waitFor(() => expect(onSubmit).toHaveBeenCalledWith('groups', 'name,rangeStart,rangeEnd\nAssets,1000,1999'))
  })

  it('renders row-level report errors and warnings', () => {
    const report: ImportReport = { total: 1, created: 0, updated: 0, failed: 1, rows: [{ rowNumber: 2, outcome: 'failed', errors: [{ code: 'account.group_unknown', message: 'Unknown group', field: 'group' }], warnings: ['Ignored ownerEmail.'] }] }
    renderModal({ report })

    expect(screen.getByText('1 rows: 0 created, 0 updated, 1 failed.')).toBeTruthy()
    expect(screen.getByText('Unknown group')).toBeTruthy()
    expect(screen.getByText('Ignored ownerEmail.')).toBeTruthy()
  })

  it('renders the permission message for a 403', () => {
    renderModal({ error: new AccountManagementError(403, [{ code: 'forbidden', message: 'Forbidden' }]) })

    expect(screen.getByRole('alert').textContent).toBe('You do not have permission to manage accounts in this space.')
  })

  it('disables cancel and submit while importing', () => {
    render(<I18nextProvider i18n={i18n}><ImportModal open submitting onClose={vi.fn()} onSubmit={vi.fn()} /></I18nextProvider>)

    expect((screen.getByRole('button', { name: 'Cancel' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Saving…' }) as HTMLButtonElement).disabled).toBe(true)
  })
})