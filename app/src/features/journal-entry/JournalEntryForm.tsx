import { useTranslation } from 'react-i18next'
import { DateField, FormField, FormSection } from '../../shared'
import type { Account } from '../../application/accounts'
import { createJournalEntrySubmission, PostingError } from '../../application/journalEntries'
import { usePostJournalEntry } from '../../application/query/usePostJournalEntry'
import { useJournalEntryForm } from './useJournalEntryForm'
import { JournalLinesEditor } from './JournalLinesEditor'

type JournalEntryFormProps = { spaceId: string; accounts: Account[] }

export function JournalEntryForm({ spaceId, accounts }: JournalEntryFormProps) {
  const { t } = useTranslation()
  const form = useJournalEntryForm(accounts)
  const mutation = usePostJournalEntry(spaceId)
  const serverError = mutation.error instanceof PostingError ? mutation.error : undefined
  const issueMessage = (code: string) => code === 'request.invalid.description' ? t('journalEntry.validation.description') : code === 'request.invalid.lines' ? t('journalEntry.validation.lines') : t(`journalEntry.validation.${code}`)
  const formIssue = form.issues.find((issue) => issue.line === null)
  const baseCurrency = accounts[0]?.currency ?? 'CHF'
  return <form onSubmit={(event) => { event.preventDefault(); if (form.isValid) mutation.mutate(createJournalEntrySubmission(form.input)) }} style={{ display: 'grid', gap: 'var(--space-4)', maxWidth: 1100 }}>
    <FormSection title={t('journalEntry.details')} style={{ display: 'grid', gridTemplateColumns: 'var(--layout-form-label) 1fr 1fr', gap: 'var(--space-3)' }}>
      <DateField id="journal-date" label={t('journalEntry.date')} value={form.state.date} onChange={(event) => form.setField('date', event.target.value)} />
      <FormField id="journal-description" label={t('journalEntry.fieldDescription')} value={form.state.description} placeholder={t('journalEntry.descriptionPlaceholder')} onChange={(event) => form.setField('description', event.target.value)} error={formIssue?.code === 'request.invalid.description' ? issueMessage(formIssue.code) : undefined} />
      <FormField id="journal-reference" label={t('journalEntry.reference')} value={form.state.reference} onChange={(event) => form.setField('reference', event.target.value)} />
    </FormSection>
    <FormSection title={t('journalEntry.lines')}><JournalLinesEditor accounts={accounts} lines={form.state.lines} issues={form.issues} currency={baseCurrency} onChange={form.updateLine} onAdd={form.addLine} onRemove={form.removeLine} /></FormSection>
    {serverError && <div role="alert" style={{ color: 'var(--color-danger)' }}>{serverError.issues.map((issue, index) => <p key={`${issue.code}-${index}`}>{issue.line === null ? issue.message : `${t('journalEntry.line')} ${issue.line + 1}: ${issue.message}`}</p>)}</div>}
    {mutation.isSuccess && <p role="status">{t('journalEntry.success', { entryNo: mutation.data.entryNo })}</p>}
    <button type="submit" disabled={!form.isValid || mutation.isPending}>{mutation.isPending ? t('journalEntry.submitting') : t('journalEntry.submit')}</button>
  </form>
}