import { useTranslation } from 'react-i18next'
import { useBalanceSheet } from '../../application/query/useBalanceSheet'
import { formatMoney } from '../../i18n/format/money'
import { StatementReportTable } from './StatementReportTable'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8a8a1'
const baseCurrency = import.meta.env.VITE_DEMO_BASE_CURRENCY || 'CHF'

export function BalanceSheetPage() {
  const { t, i18n } = useTranslation()
  const reportQuery = useBalanceSheet(demoSpaceId)

  if (reportQuery.isPending) return <p role="status">{t('balanceSheetPage.loading')}</p>
  if (reportQuery.isError) throw reportQuery.error

  const report = reportQuery.data
  return <section className="balance-sheet-page">
    <p className="eyebrow">{t('balanceSheetPage.eyebrow')}</p>
    <h1>{t('balanceSheetPage.title')}</h1>
    <p className="lead">{t('balanceSheetPage.description')}</p>
    <StatementReportTable lines={report.lines} locale={i18n.language} currency={baseCurrency} tableLabel={t('balanceSheetPage.tableLabel')} emptyLabel={t('balanceSheetPage.empty')} />
    {report.lines.length > 0 && <dl className="report-summary">
      <div><dt>{t('balanceSheetPage.result')}</dt><dd>{formatMoney(report.currentResultMinor, baseCurrency, i18n.language)}</dd></div>
    </dl>}
  </section>
}