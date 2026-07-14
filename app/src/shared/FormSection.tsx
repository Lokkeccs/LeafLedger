import type { CSSProperties, ReactNode } from 'react'

export type FormSectionProps = { title?: ReactNode; children: ReactNode; style?: CSSProperties; titleStyle?: CSSProperties }

export function FormSection({ title, children, style, titleStyle }: FormSectionProps) {
  return <section style={{ border: '1px solid var(--color-line)', borderRadius: 'var(--radius-md)', padding: 'var(--space-3)', ...style }}>
    {title != null && title !== '' && <div style={{ fontWeight: 700, marginBottom: 'var(--space-3)', fontSize: 13, ...titleStyle }}>{title}</div>}
    {children}
  </section>
}