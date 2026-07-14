function currencyFractionDigits(currency: string, locale: string): number {
  return new Intl.NumberFormat(locale, { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2
}

export function formatMoney(minorUnits: number, currency: string, locale: string): string {
  if (!Number.isSafeInteger(minorUnits)) throw new RangeError('Money minor units must be a safe integer')

  const fractionDigits = currencyFractionDigits(currency, locale)
  const negative = minorUnits < 0
  const digits = Math.abs(minorUnits).toString().padStart(fractionDigits + 1, '0')
  const splitAt = Math.max(0, digits.length - fractionDigits)
  const majorDigits = digits.slice(0, splitAt) || '0'
  const minorDigits = digits.slice(splitAt).padStart(fractionDigits, '0')
  const major = Number(majorDigits)
  if (!Number.isSafeInteger(major)) throw new RangeError('Money major units exceed safe integer precision')

  const formatter = new Intl.NumberFormat(locale, {
    style: 'currency', currency, minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits,
  })
  const parts = formatter.formatToParts(negative ? -major : major)
  return parts.map((part) => part.type === 'fraction' ? minorDigits : part.value).join('')
}