import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'
import { useAccounts } from '../../application/query/useAccounts'
import { useAccountLedger } from '../../application/query/useAccountLedger'
import type { AccountLedgerLine } from '../../application/reports'
import { formatMoney } from '../../i18n/format/money'
import { DataTable, DateField, type DataColumn } from '../../shared'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8a8a1'
const baseCurrency = import.meta.env.VITE_DEMO_BASE_CURRENCY || 'CHF'

export function AccountLedgerPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { accountId } = useParams<{ accountId: string }>()
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const accountsQuery = useAccounts(demoSpaceId)
  const ledgerQuery = useAccountLedger(demoSpaceId, accountId, { ...(from ? { from } : {}), ...(to ? { to } : {}) })

  if (accountsQuery.isError) throw accountsQuery.error
  if (ledgerQuery.isError) throw ledgerQuery.error
  if (accountsQuery.isPending || ledgerQuery.isPending) return <p role="status">{t('accountLedgerPage.loading')}</p>

  const report = ledgerQuery.data
  const columns: DataColumn<AccountLedgerLine>[] = [
    { header: t('accountLedgerPage.columns.date'), render: (line) => line.date, width: 130 },
    { header: t('accountLedgerPage.columns.entryNo'), render: (line) => line.entryNo, width: 100 },
    { header: t('accountLedgerPage.columns.description'), render: (line) => [line.description, line.reference].filter(Boolean).join(' · ') || '-', width: 280 },
    { header: t('accountLedgerPage.columns.amount'), render: (line) => formatMoney(line.amountMinor, line.lineCurrency, i18n.language), align: 'right', width: 160 },
    { header: t('accountLedgerPage.columns.baseAmount'), render: (line) => formatMoney(line.baseAmountMinor, baseCurrency, i18n.language), align: 'right', width: 160 },
    { header: t('accountLedgerPage.columns.runningBalance'), render: (line) => formatMoney(line.runningBalanceMinor, baseCurrency, i18n.language), align: 'right', width: 180 },
  ]
  const emptyState = <p role="status">{t('accountLedgerPage.empty')}</p>

  return <section className="account-ledger-page">
    <p className="eyebrow">{t('accountLedgerPage.eyebrow')}</p>
    <h1>{report?.accountName || t('accountLedgerPage.title')}</h1>
    <p className="lead">{t('accountLedgerPage.description')}</p>
    <div className="account-ledger-filters">
      <label htmlFor="account-ledger-account" className="account-ledger-account-field">
        <span>{t('accountLedgerPage.account')}</span>
        <select id="account-ledger-account" value={accountId ?? ''} onChange={(event) => navigate(`/reports/account/${event.target.value}`)}>
          <option value="">{t('accountLedgerPage.selectAccount')}</option>
          {accountsQuery.data.map((account) => <option key={account.id} value={account.id}>{account.code} · {account.name}</option>)}
        </select>
      </label>
      <DateField id="account-ledger-from" label={t('accountLedgerPage.from')} value={from} onChange={(event) => setFrom(event.target.value)} />
      <DateField id="account-ledger-to" label={t('accountLedgerPage.to')} value={to} onChange={(event) => setTo(event.target.value)} />
    </div>
    {accountId && report && <>
      <dl className="report-summary">
        <div><dt>{t('accountLedgerPage.opening')}</dt><dd>{formatMoney(report.openingBalanceMinor, baseCurrency, i18n.language)}</dd></div>
        <div><dt>{t('accountLedgerPage.closing')}</dt><dd>{formatMoney(report.closingBalanceMinor, baseCurrency, i18n.language)}</dd></div>
      </dl>
      <DataTable data={report.lines} rows={report.lines} columns={columns} rowKey={(line) => `${line.entryId}-${line.entryNo}-${line.date}`} emptyState={emptyState} noMatchState={emptyState} ariaLabel={t('accountLedgerPage.tableLabel')} />
    </>}
  </section>
}