import { describe, expect, it } from 'vitest'
import { formatDate, formatNumber } from './datetime'
import { formatMoney } from './money'

describe('render-edge formatters', () => {
  it('formats minor units without floating-point arithmetic', () => {
    expect(formatMoney(12345, 'USD', 'en-US')).toBe('$123.45')
    expect(formatMoney(-12345, 'USD', 'de-DE')).toBe('-123,45 $')
    expect(formatMoney(0, 'JPY', 'ja-JP')).toBe('￥0')
    expect(formatMoney(1234, 'BHD', 'en-US')).toBe('BHD 1.234')
    expect(formatMoney(9007199254740991, 'USD', 'en-US')).not.toContain('NaN')
  })

  it('uses the requested locale for dates and numbers', () => {
    const date = new Date('2026-01-14T00:00:00.000Z')
    expect(formatNumber(1234567.89, 'en-US')).toBe('1,234,567.89')
    expect(formatNumber(1234567.89, 'de-DE')).toBe('1.234.567,89')
    expect(formatDate(date, 'en-US')).not.toBe(formatDate(date, 'de-DE'))
  })
})