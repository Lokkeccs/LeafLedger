import type { CSSProperties } from 'react'

export const thStyle: CSSProperties = {
  padding: 'var(--space-3) var(--space-4)',
  color: 'var(--color-muted)',
  borderBottom: '1px solid var(--color-line)',
  fontSize: 12,
  fontWeight: 700,
  letterSpacing: '.08em',
  textAlign: 'left',
  textTransform: 'uppercase',
  whiteSpace: 'nowrap',
}

export const tdStyle: CSSProperties = {
  padding: 'var(--space-4)',
  verticalAlign: 'top',
}

export const tdStyleMiddle: CSSProperties = {
  ...tdStyle,
  verticalAlign: 'middle',
}