import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { useTrialBalance } from '../../application/query/useTrialBalance'
import type { TrialBalanceRow } from '../../application/reports'
import { formatMoney } from '../../i18n/format/money'
import { DataTable, type DataColumn } from '../../shared'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8a8a1'
const baseCurrency = import.meta.env.VITE_DEMO_BASE_CURRENCY || 'CHF'

export function TrialBalancePage() {
  const { t, i18n } = useTranslation()
  const reportQuery = useTrialBalance(demoSpaceId)

  if (reportQuery.isPending) return <p role="status">{t('trialBalancePage.loading')}</p>
  if (reportQuery.isError) throw reportQuery.error

  const report = reportQuery.data
  const debitTotal = report.rows.reduce((total, row) => total + (row.baseBalanceMinor > 0 ? row.baseBalanceMinor : 0), 0)
  const creditTotal = report.rows.reduce((total, row) => total + (row.baseBalanceMinor < 0 ? -row.baseBalanceMinor : 0), 0)
  const columns: DataColumn<TrialBalanceRow>[] = [
    { header: t('trialBalancePage.columns.code'), render: (row) => row.accountCode, width: 100 },
    { header: t('trialBalancePage.columns.name'), render: (row) => <Link to={`/reports/account/${row.accountId}`}>{row.accountName}</Link>, width: 280 },
    { header: t('trialBalancePage.columns.kind'), render: (row) => row.accountKind, width: 160 },
    { header: t('trialBalancePage.columns.debit'), render: (row) => row.baseBalanceMinor > 0 ? formatMoney(row.baseBalanceMinor, baseCurrency, i18n.language) : '-', align: 'right', width: 160 },
    { header: t('trialBalancePage.columns.credit'), render: (row) => row.baseBalanceMinor < 0 ? formatMoney(-row.baseBalanceMinor, baseCurrency, i18n.language) : '-', align: 'right', width: 160 },
  ]
  const emptyState = <p role="status">{t('trialBalancePage.empty')}</p>
  const balanced = report.totalBaseBalanceMinor === 0

  return <section className="trial-balance-page">
    <p className="eyebrow">{t('trialBalancePage.eyebrow')}</p>
    <h1>{t('trialBalancePage.title')}</h1>
    <p className="lead">{t('trialBalancePage.description')}</p>
    <DataTable data={report.rows} rows={report.rows} columns={columns} rowKey={(row) => row.accountId} emptyState={emptyState} noMatchState={emptyState} ariaLabel={t('trialBalancePage.tableLabel')} />
    {report.rows.length > 0 && <dl className="report-summary">
      <div><dt>{t('trialBalancePage.totals.debit')}</dt><dd>{formatMoney(debitTotal, baseCurrency, i18n.language)}</dd></div>
      <div><dt>{t('trialBalancePage.totals.credit')}</dt><dd>{formatMoney(creditTotal, baseCurrency, i18n.language)}</dd></div>
      <div><dt>{t('trialBalancePage.totals.net')}</dt><dd>{formatMoney(report.totalBaseBalanceMinor, baseCurrency, i18n.language)}</dd></div>
      <div><dt>{t('trialBalancePage.totals.status')}</dt><dd role="status">{balanced ? t('trialBalancePage.balanced') : t('trialBalancePage.unbalanced')}</dd></div>
    </dl>}
  </section>
}