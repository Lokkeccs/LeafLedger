import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { ImportReport } from '../../application/accountImport'
import { AccountManagementError } from '../../application/accounts'
import { DataTable, FormField, ModalShell, type ColumnDef } from '../../shared'

type ImportModalProps = {
  open: boolean
  submitting: boolean
  report?: ImportReport | undefined
  error?: unknown
  onClose: () => void
  onSubmit: (kind: 'accounts' | 'groups', csv: string) => Promise<void>
}

export function ImportModal({ open, submitting, report, error, onClose, onSubmit }: ImportModalProps) {
  const { t } = useTranslation()
  const [fileName, setFileName] = useState('')
  const [csv, setCsv] = useState('')
  const [kind, setKind] = useState<'accounts' | 'groups'>('accounts')
  const [clientError, setClientError] = useState('')
  const managementError = error instanceof AccountManagementError ? error : undefined

  async function selectFile(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return
    setFileName(file.name)
    setCsv(await file.text())
    setClientError('')
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!csv.trim()) { setClientError(t('accountsPage.import.emptyFile')); return }
    const firstLine = csv.split(/\r?\n/, 1)[0]?.trim()
    if (!firstLine || !firstLine.includes(',')) { setClientError(t('accountsPage.import.invalidHeader')); return }
    setClientError('')
    try {
      await onSubmit(kind, csv)
    } catch (submitError) {
      if (!(submitError instanceof AccountManagementError)) throw submitError
    }
  }

  const columns: ColumnDef<ImportReport['rows'][number]>[] = [
    { header: t('accountsPage.import.row'), render: (row) => row.rowNumber, width: 70 },
    { header: t('accountsPage.import.outcome'), render: (row) => row.outcome, width: 110 },
    { header: t('accountsPage.import.errors'), render: (row) => row.errors.map((issue) => issue.message).join('; ') || t('common.no'), width: 280 },
    { header: t('accountsPage.import.warnings'), render: (row) => row.warnings.join('; ') || t('common.no'), width: 280 },
  ]

  return <ModalShell open={open} {...(submitting ? {} : { onClose })} closeLabel={t('common.close')} title={t('accountsPage.import.title')} maxWidth={900}>
    <form id="accounts-import-form" onSubmit={(event) => void submit(event)} style={{ display: 'grid', gap: 'var(--space-3)' }}>
      <FormField id="accounts-import-file" label={t('accountsPage.import.file')} type="file" accept=".csv,text/csv" onChange={(event) => void selectFile(event)} />
      <FormField id="accounts-import-kind" label={t('accountsPage.import.kind')} control={<select value={kind} onChange={(event) => setKind(event.target.value as 'accounts' | 'groups')}><option value="accounts">{t('accountsPage.import.accounts')}</option><option value="groups">{t('accountsPage.import.groups')}</option></select>} />
      {fileName && <p>{fileName}</p>}
      {(clientError || managementError?.status === 403) && <p role="alert" style={{ color: 'var(--color-danger)' }}>{clientError || t('accountsPage.permissionDenied')}</p>}
      {managementError && managementError.status !== 403 && <p role="alert" style={{ color: 'var(--color-danger)' }}>{t('accountsPage.import.serverError')}</p>}
      {report && <section aria-label={t('accountsPage.import.report')}>
        <p>{t('accountsPage.import.summary', report)}</p>
        <DataTable data={report.rows} rows={report.rows} columns={columns} rowKey={(row) => row.rowNumber} emptyState={<p>{t('common.noData')}</p>} noMatchState={<p>{t('common.noResults')}</p>} ariaLabel={t('accountsPage.import.report')} />
      </section>}
    </form>
    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 'var(--space-2)' }}>
      <button type="button" onClick={onClose} disabled={submitting}>{t('common.cancel')}</button>
      <button type="submit" form="accounts-import-form" disabled={submitting}>{submitting ? t('common.saving') : t('accountsPage.import.submit')}</button>
    </div>
  </ModalShell>
}