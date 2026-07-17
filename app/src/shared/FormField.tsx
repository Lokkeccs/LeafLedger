import { cloneElement, type InputHTMLAttributes, type ReactElement, type ReactNode } from 'react'

export type FormFieldProps = InputHTMLAttributes<HTMLInputElement> & { id: string; label: ReactNode; error?: ReactNode; description?: ReactNode; control?: ReactElement }

export function FormField({ id, label, error, description, control, ...inputProps }: FormFieldProps) {
  const describedBy = [description && `${id}-description`, error && `${id}-error`].filter(Boolean).join(' ') || undefined
  const fieldControl = control
    ? cloneElement(control as ReactElement<Record<string, unknown>>, { id, 'aria-invalid': error ? true : undefined, 'aria-describedby': describedBy })
    : <input {...inputProps} id={id} aria-invalid={error ? true : undefined} aria-describedby={describedBy} style={{ padding: 'var(--space-2) var(--space-3)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', background: 'var(--color-surface)', color: 'var(--color-text)', ...inputProps.style }} />

  return <label htmlFor={id} style={{ display: 'grid', gap: 'var(--space-1)' }}>
    <span style={{ fontWeight: 700, fontSize: 13 }}>{label}</span>
    {fieldControl}
    {description && <span id={`${id}-description`} style={{ color: 'var(--color-text-muted)', fontSize: 13 }}>{description}</span>}
    {error && <span id={`${id}-error`} role="alert" style={{ color: 'var(--color-danger)', fontSize: 13 }}>{error}</span>}
  </label>
}