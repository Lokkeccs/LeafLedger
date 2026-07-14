import type { InputHTMLAttributes, ReactNode } from 'react'

export type DateFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { id: string; label: ReactNode; error?: ReactNode }

export function DateField({ id, label, error, ...props }: DateFieldProps) {
  return <label htmlFor={id} style={{ display: 'grid', gap: 'var(--space-1)' }}>
    <span style={{ fontWeight: 700, fontSize: 13 }}>{label}</span>
    <input {...props} id={id} type="date" aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined} style={{ padding: 'var(--space-2) var(--space-3)', border: '1px solid var(--color-line)', borderRadius: 'var(--radius-sm)', background: 'var(--color-surface)', color: 'var(--color-ink)', ...props.style }} />
    {error && <span id={`${id}-error`} role="alert" style={{ color: 'var(--color-danger)', fontSize: 13 }}>{error}</span>}
  </label>
}