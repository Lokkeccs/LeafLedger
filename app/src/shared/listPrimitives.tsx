import type { ReactNode } from 'react'

export function ActionCell({ children }: { children: ReactNode }) {
  return <td style={{ ...cellStyle, textAlign: 'right' }}>{children}</td>
}

const cellStyle = {
  padding: 'var(--space-3) var(--space-4)',
  verticalAlign: 'middle' as const,
  whiteSpace: 'nowrap' as const,
}