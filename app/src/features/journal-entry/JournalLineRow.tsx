import { useTranslation } from 'react-i18next'
import type { Account } from '../../application/accounts'
import type { JournalFormLine } from './useJournalEntryForm'
import { AccountPicker } from './AccountPicker'
import { MoneyInput } from '../../shared'

type JournalLineRowProps = { accounts: Account[]; line: JournalFormLine; index: number; currency: string; onChange: (patch: Partial<Omit<JournalFormLine, 'id'>>) => void; onRemove: () => void; canRemove: boolean; issue?: string | undefined }

export function JournalLineRow({ accounts, line, index, currency, onChange, onRemove, canRemove, issue }: JournalLineRowProps) {
  const { t } = useTranslation()
  const debit = line.amountMinor > 0 ? line.amountMinor : 0
  const credit = line.amountMinor < 0 ? Math.abs(line.amountMinor) : 0
  return <div style={{ display: 'grid', gridTemplateColumns: 'minmax(240px, 1fr) 160px 160px auto', gap: 'var(--space-2)', alignItems: 'end' }}>
    <label htmlFor={`journal-line-${index}-account`} style={{ display: 'grid', gap: 'var(--space-1)' }}><span style={{ fontWeight: 700, fontSize: 13 }}>{t('journalEntry.account')}</span><AccountPicker id={`journal-line-${index}-account`} accounts={accounts} value={line.accountId} onChange={(accountId, accountCurrency) => onChange({ accountId, currency: accountCurrency })} error={issue} />{issue && <span role="alert" style={{ color: 'var(--color-danger)', fontSize: 13 }}>{t(`journalEntry.validation.${issue}`)}</span>}</label>
    <MoneyInput id={`journal-line-${index}-debit`} label={t('journalEntry.debit')} value={debit} currency={currency} onChange={(value) => onChange({ amountMinor: value })} />
    <MoneyInput id={`journal-line-${index}-credit`} label={t('journalEntry.credit')} value={credit} currency={currency} onChange={(value) => onChange({ amountMinor: -value })} />
    <button type="button" onClick={onRemove} disabled={!canRemove} aria-label={t('journalEntry.removeLine')}>{t('common.remove')}</button>
  </div>
}