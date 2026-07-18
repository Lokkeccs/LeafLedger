import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import type { ReportLine } from '../../application/reports'
import { formatMoney } from '../../i18n/format/money'
import { DataTable, type DataColumn } from '../../shared'

type StatementReportTableProps = {
  lines: ReportLine[]
  locale: string
  currency: string
  tableLabel: string
  emptyLabel: string
}

export function StatementReportTable({ lines, locale, currency, tableLabel, emptyLabel }: StatementReportTableProps) {
  const { t } = useTranslation()
  const groups = [...new Set(lines.map((line) => line.accountKind))]
  const columns: DataColumn<ReportLine>[] = [
    { header: t('statementReport.columns.code'), render: (line) => line.accountCode ?? '-', width: 100 },
    { header: t('statementReport.columns.name'), render: (line) => line.name, width: 320 },
    { header: t('statementReport.columns.kind'), render: (line) => line.accountKind, width: 160 },
    { header: t('statementReport.columns.amount'), render: (line) => formatMoney(line.amountMinor, currency, locale), align: 'right', width: 180 },
  ]
  const rowStyle = (line: ReportLine): CSSProperties | undefined => line.isDerived
    ? { fontWeight: 'var(--font-weight-semibold)' as CSSProperties['fontWeight'] }
    : undefined

  if (lines.length === 0) return <p role="status">{emptyLabel}</p>

  return <div className="statement-report-tables">
    {groups.map((group) => {
      const groupLines = lines.filter((line) => line.accountKind === group)
      const groupLabel = t(`statementReport.kinds.${group}`, { defaultValue: group })
      return <section key={group} aria-labelledby={`statement-group-${group}`}>
        <h2 id={`statement-group-${group}`}>{groupLabel}</h2>
        <DataTable
          data={groupLines}
          rows={groupLines}
          columns={columns}
          rowKey={(line) => `${line.accountId ?? 'derived'}-${line.name}`}
          emptyState={<p role="status">{emptyLabel}</p>}
          noMatchState={<p role="status">{emptyLabel}</p>}
          rowStyle={rowStyle}
          ariaLabel={`${tableLabel} - ${group}`}
        />
      </section>
    })}
  </div>
}