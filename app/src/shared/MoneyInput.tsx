import { useState, type InputHTMLAttributes } from 'react'
import { useMoneyFormat } from '../i18n/hooks'

export type MoneyInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'> & { value: number; currency: string; onChange: (minorUnits: number) => void; label?: string }

function fractionDigits(currency: string): number {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2
}

function minorUnitsToInput(value: number, currency: string): string {
  const digits = fractionDigits(currency)
  const negative = value < 0
  const absolute = BigInt(Math.abs(value)).toString().padStart(digits + 1, '0')
  const splitAt = Math.max(0, absolute.length - digits)
  const major = absolute.slice(0, splitAt) || '0'
  const minor = absolute.slice(splitAt).padStart(digits, '0')
  return `${negative ? '-' : ''}${major}${digits ? `.${minor}` : ''}`
}

function parseMinorUnits(input: string, currency: string): number | null {
  const normalized = input.trim().replace(',', '.')
  const match = /^(-?)(\d*)(?:\.(\d*))?$/.exec(normalized)
  if (!match || (!match[2] && !match[3])) return null
  const digits = fractionDigits(currency)
  const major = match[2] || '0'
  const minor = (match[3] || '').padEnd(digits, '0')
  if (minor.length > digits || /\D/.test(major + minor)) return null
  const combined = BigInt(`${match[1] === '-' ? '-' : ''}${major}${digits ? minor : ''}`)
  const result = Number(combined)
  return Number.isSafeInteger(result) ? result : null
}

export function MoneyInput({ value, currency, onChange, label, id, onFocus, onBlur, ...props }: MoneyInputProps) {
  const formatMoney = useMoneyFormat()
  const [raw, setRaw] = useState<string | null>(null)
  const inputId = id ?? 'money-input'
  const input = <input {...props} id={inputId} type="text" inputMode="decimal" value={raw ?? formatMoney(value, currency)} onFocus={(event) => { setRaw(minorUnitsToInput(value, currency)); onFocus?.(event) }} onChange={(event) => { const next = event.target.value; setRaw(next); const parsed = parseMinorUnits(next, currency); if (parsed !== null) onChange(parsed) }} onBlur={(event) => { setRaw(null); onBlur?.(event) }} />
  return label ? <label htmlFor={inputId} style={{ display: 'grid', gap: 'var(--space-1)' }}><span style={{ fontWeight: 700, fontSize: 13 }}>{label}</span>{input}</label> : input
}