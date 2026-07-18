import { useTranslation } from 'react-i18next'
import { useIncomeStatement } from '../../application/query/useIncomeStatement'
import { formatMoney } from '../../i18n/format/money'
import { StatementReportTable } from './StatementReportTable'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8a8a1'
const baseCurrency = import.meta.env.VITE_DEMO_BASE_CURRENCY || 'CHF'

export function IncomeStatementPage() {
  const { t, i18n } = useTranslation()
  const reportQuery = useIncomeStatement(demoSpaceId)

  if (reportQuery.isPending) return <p role="status">{t('incomeStatementPage.loading')}</p>
  if (reportQuery.isError) throw reportQuery.error

  const report = reportQuery.data
  return <section className="income-statement-page">
    <p className="eyebrow">{t('incomeStatementPage.eyebrow')}</p>
    <h1>{t('incomeStatementPage.title')}</h1>
    <p className="lead">{t('incomeStatementPage.description')}</p>
    <StatementReportTable lines={report.lines} locale={i18n.language} currency={baseCurrency} tableLabel={t('incomeStatementPage.tableLabel')} emptyLabel={t('incomeStatementPage.empty')} />
    {report.lines.length > 0 && <dl className="report-summary">
      <div><dt>{t('incomeStatementPage.result')}</dt><dd>{formatMoney(report.netResultMinor, baseCurrency, i18n.language)}</dd></div>
    </dl>}
  </section>
}