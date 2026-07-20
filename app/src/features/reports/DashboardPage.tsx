import { useTranslation } from 'react-i18next'
import { useDashboardSummary } from '../../application/query/useDashboardSummary'
import type { DashboardSummary } from '../../application/reports'
import { formatMoney } from '../../i18n/format/money'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8a8a1'
const baseCurrency = import.meta.env.VITE_DEMO_BASE_CURRENCY || 'CHF'

type MoneyKey = 'totalAssetsMinor' | 'totalLiabilitiesMinor' | 'totalEquityMinor' | 'totalIncomeMinor' | 'totalExpensesMinor' | 'netResultMinor' | 'netWorthMinor'

function MoneyStat({ label, value, locale, accent }: { label: string; value: number; locale: string; accent?: boolean }) {
  return <div className={`dashboard-stat${accent ? ' dashboard-stat-accent' : ''}`}>
    <dt>{label}</dt>
    <dd>{formatMoney(value, baseCurrency, locale)}</dd>
  </div>
}

function DashboardStats({ report, locale }: { report: DashboardSummary; locale: string }) {
  const moneyStats: Array<{ key: MoneyKey; label: string; accent?: boolean }> = [
    { key: 'totalAssetsMinor', label: 'dashboardPage.metrics.assets' },
    { key: 'totalLiabilitiesMinor', label: 'dashboardPage.metrics.liabilities' },
    { key: 'totalEquityMinor', label: 'dashboardPage.metrics.equity' },
    { key: 'totalIncomeMinor', label: 'dashboardPage.metrics.income' },
    { key: 'totalExpensesMinor', label: 'dashboardPage.metrics.expenses' },
    { key: 'netResultMinor', label: 'dashboardPage.metrics.netResult', accent: true },
    { key: 'netWorthMinor', label: 'dashboardPage.metrics.netWorth', accent: true },
  ]
  const { t } = useTranslation()

  return <>
    <dl className="dashboard-stats">
      {moneyStats.map(({ key, label, accent }) => <MoneyStat key={key} label={t(label)} value={report[key]} locale={locale} {...(accent ? { accent: true } : {})} />)}
      <div className="dashboard-stat dashboard-stat-count">
        <dt>{t('dashboardPage.metrics.accountCount')}</dt>
        <dd>{report.accountCount}</dd>
      </div>
    </dl>
    <div className={`dashboard-integrity ${report.balanced ? 'dashboard-integrity-balanced' : 'dashboard-integrity-unbalanced'}`} role="status">
      <span className="dashboard-integrity-mark" aria-hidden="true" />
      <span>{report.balanced ? t('dashboardPage.balanced') : t('dashboardPage.unbalanced')}</span>
    </div>
  </>
}

export function DashboardPage() {
  const { t, i18n } = useTranslation()
  const reportQuery = useDashboardSummary(demoSpaceId)

  if (reportQuery.isPending) return <p role="status">{t('dashboardPage.loading')}</p>
  if (reportQuery.isError) throw reportQuery.error

  const report = reportQuery.data
  const isEmpty = report.accountCount === 0 && report.totalAssetsMinor === 0 && report.totalLiabilitiesMinor === 0 && report.totalEquityMinor === 0 && report.totalIncomeMinor === 0 && report.totalExpensesMinor === 0 && report.netResultMinor === 0 && report.netWorthMinor === 0

  return <section className="dashboard-page">
    <div className="dashboard-heading">
      <div>
        <p className="eyebrow">{t('dashboardPage.eyebrow')}</p>
        <h1>{t('dashboardPage.title')}</h1>
        <p className="lead">{t('dashboardPage.description')}</p>
      </div>
      <div className="dashboard-period" aria-label={t('dashboardPage.periodLabel')}>
        <span>{t('dashboardPage.periodLabel')}</span>
        <strong>{t('dashboardPage.periodValue')}</strong>
      </div>
    </div>
    {isEmpty ? <p className="dashboard-empty" role="status">{t('dashboardPage.empty')}</p> : <DashboardStats report={report} locale={i18n.language} />}
    {isEmpty && <div className={`dashboard-integrity ${report.balanced ? 'dashboard-integrity-balanced' : 'dashboard-integrity-unbalanced'}`} role="status">
      <span className="dashboard-integrity-mark" aria-hidden="true" />
      <span>{report.balanced ? t('dashboardPage.balanced') : t('dashboardPage.unbalanced')}</span>
    </div>}
  </section>
}