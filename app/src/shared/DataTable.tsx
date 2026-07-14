import type { CSSProperties, MouseEvent, ReactNode } from 'react'
import { ActionCell } from './listPrimitives'
import { tdStyle, tdStyleMiddle, thStyle } from './tableStyles'

export type DataColumn<T> = {
  type?: 'data'
  header: string
  render: (row: T) => ReactNode
  width?: number | string
  align?: CSSProperties['textAlign']
  verticalAlign?: 'top' | 'middle'
  cellStyle?: CSSProperties
}

export type ActionsColumn<T> = {
  type: 'actions'
  render: (row: T) => ReactNode
  width?: number | string
}

export type ColumnDef<T> = DataColumn<T> | ActionsColumn<T>

export type DataTableProps<T> = {
  data: T[]
  rows: T[]
  columns: ColumnDef<T>[]
  rowKey: (row: T) => string | number
  emptyState: ReactNode
  noMatchState: ReactNode
  onRowClick?: (row: T, event: MouseEvent<HTMLTableRowElement>) => void
  cellAlign?: 'top' | 'middle'
  rowStyle?: (row: T) => CSSProperties | undefined
  className?: string
  ariaLabel?: string
}

export function DataTable<T>({ data, rows, columns, rowKey, emptyState, noMatchState, onRowClick, cellAlign = 'middle', rowStyle, className, ariaLabel }: DataTableProps<T>) {
  if (data.length === 0) return <>{emptyState}</>
  if (rows.length === 0) return <>{noMatchState}</>

  const hasColgroup = columns.some((column) => column.width !== undefined)
  const baseRowStyle: CSSProperties = {
    borderBottom: '1px solid var(--color-line)',
    cursor: onRowClick ? 'pointer' : undefined,
  }

  return <div className={`table-card${className ? ` ${className}` : ''}`} style={{ overflowX: 'auto', overflowY: 'hidden' }}>
    <table style={{ width: 'max-content', minWidth: '100%', borderCollapse: 'collapse' }} aria-label={ariaLabel}>
      {hasColgroup && <colgroup>{columns.map((column, index) => <col key={index} style={column.width !== undefined ? { width: column.width } : undefined} />)}</colgroup>}
      <thead><tr>{columns.map((column, index) => column.type === 'actions'
        ? <th key={index} style={{ ...thStyle, width: column.width ?? 88 }} />
        : <th key={column.header || index} style={column.align ? { ...thStyle, textAlign: column.align } : thStyle}>{column.header}</th>)}</tr></thead>
      <tbody>{rows.map((row) => <tr key={rowKey(row)} style={rowStyle ? { ...baseRowStyle, ...rowStyle(row) } : baseRowStyle} onClick={onRowClick ? (event) => onRowClick(row, event) : undefined}>
        {columns.map((column, index) => column.type === 'actions'
          ? <ActionCell key={index}>{column.render(row)}</ActionCell>
          : <td key={column.header || index} style={{ ...(column.verticalAlign ?? cellAlign) === 'top' ? tdStyle : tdStyleMiddle, ...(column.align ? { textAlign: column.align } : undefined), ...column.cellStyle }}>{column.render(row)}</td>)}
      </tr>)}</tbody>
    </table>
  </div>
}