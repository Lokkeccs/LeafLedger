import { useTranslation } from 'react-i18next'
import type { Account } from '../../application/accounts'
import type { MirrorIssue } from './balanceMirror'
import type { JournalFormLine } from './useJournalEntryForm'
import { JournalLineRow } from './JournalLineRow'

type JournalLinesEditorProps = { accounts: Account[]; lines: JournalFormLine[]; issues: MirrorIssue[]; currency: string; onChange: (id: string, patch: Partial<Omit<JournalFormLine, 'id'>>) => void; onAdd: () => void; onRemove: (id: string) => void }

export function JournalLinesEditor({ accounts, lines, issues, currency, onChange, onAdd, onRemove }: JournalLinesEditorProps) {
  const { t } = useTranslation()
  const lineIssue = (index: number) => issues.find((issue) => issue.line === index)
  const balanceIssue = issues.find((issue) => issue.code === 'entry.unbalanced')
  return <section aria-label={t('journalEntry.lines')} style={{ display: 'grid', gap: 'var(--space-3)' }}>
    <div style={{ display: 'grid', gap: 'var(--space-3)' }}>{lines.map((line, index) => <JournalLineRow key={line.id} accounts={accounts} line={line} index={index} currency={currency} onChange={(patch) => onChange(line.id, patch)} onRemove={() => onRemove(line.id)} canRemove={lines.length > 2} issue={lineIssue(index)?.code} />)}</div>
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}><button type="button" onClick={onAdd}>{t('journalEntry.addLine')}</button>{balanceIssue && <span role="alert" style={{ color: 'var(--color-danger)' }}>{t('journalEntry.validation.entryUnbalanced')}</span>}</div>
  </section>
}