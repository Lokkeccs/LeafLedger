import { useTranslation } from 'react-i18next'
import { useAccounts } from '../../application/query/useAccounts'
import { JournalEntryForm } from './JournalEntryForm'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1'

export function JournalEntryPage() {
  const { t } = useTranslation()
  const accountsQuery = useAccounts(demoSpaceId)
  if (accountsQuery.isPending) return <p role="status">{t('journalEntry.loadingAccounts')}</p>
  if (accountsQuery.isError) throw accountsQuery.error
  return <section className="journal-entry-page"><p className="eyebrow">{t('journalEntry.eyebrow')}</p><h1>{t('journalEntry.title')}</h1><p className="lead">{t('journalEntry.description')}</p><JournalEntryForm spaceId={demoSpaceId} accounts={accountsQuery.data} /></section>
}