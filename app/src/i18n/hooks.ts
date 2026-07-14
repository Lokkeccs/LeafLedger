import { useTranslation } from 'react-i18next'
import { formatDate, formatNumber } from './format/datetime'
import { formatMoney } from './format/money'

export function useMoneyFormat() {
  const { i18n } = useTranslation()
  return (minorUnits: number, currency: string) => formatMoney(minorUnits, currency, i18n.language)
}

export function useDateFormat() {
  const { i18n } = useTranslation()
  return (date: Date | number, options?: Intl.DateTimeFormatOptions) => formatDate(date, i18n.language, options)
}

export function useNumberFormat() {
  const { i18n } = useTranslation()
  return (value: number, options?: Intl.NumberFormatOptions) => formatNumber(value, i18n.language, options)
}