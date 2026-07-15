import { useTranslation } from 'react-i18next'
import { useAccounts } from '../../application/query/useAccounts'
import type { Account } from '../../application/accounts'
import { DataTable, type DataColumn } from '../../shared'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1'

export function AccountsPage() {
  const { t } = useTranslation()
  const accountsQuery = useAccounts(demoSpaceId)

  if (accountsQuery.isPending) return <p role="status">{t('accountsPage.loading')}</p>
  if (accountsQuery.isError) throw accountsQuery.error

  const accounts = accountsQuery.data
  const columns: DataColumn<Account>[] = [
    { header: t('accountsPage.columns.code'), render: (account) => account.code, width: 100 },
    { header: t('accountsPage.columns.name'), render: (account) => account.name, width: 280 },
    { header: t('accountsPage.columns.currency'), render: (account) => account.currency, width: 120 },
    { header: t('accountsPage.columns.kind'), render: (account) => account.kind, width: 160 },
    { header: t('accountsPage.columns.active'), render: (account) => account.isActive ? t('common.active') : t('common.inactive'), width: 120 },
  ]
  const emptyState = <p role="status">{t('accountsPage.empty')}</p>

  return <section className="accounts-page">
    <p className="eyebrow">{t('accountsPage.eyebrow')}</p>
    <h1>{t('accountsPage.title')}</h1>
    <p className="lead">{t('accountsPage.description')}</p>
    <DataTable data={accounts} rows={accounts} columns={columns} rowKey={(account) => account.id} emptyState={emptyState} noMatchState={emptyState} ariaLabel={t('accountsPage.tableLabel')} />
  </section>
}