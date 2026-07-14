import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import de from './locales/de.json'
import en from './locales/en.json'

export const supportedLocales = [
  { code: 'en', label: 'English' },
  { code: 'de', label: 'Deutsch' },
] as const

export type SupportedLocale = typeof supportedLocales[number]['code']
export const languageStorageKey = 'll.language'

const resources = {
  en: { translation: en },
  de: { translation: de },
} as const

function savedLanguage(): SupportedLocale {
  const saved = globalThis.localStorage?.getItem(languageStorageKey)
  return supportedLocales.some((locale) => locale.code === saved) ? saved as SupportedLocale : 'en'
}

void i18n.use(initReactI18next).init({
  resources,
  lng: savedLanguage(),
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
})

export async function setLanguage(language: string): Promise<void> {
  const selected = supportedLocales.some((locale) => locale.code === language) ? language as SupportedLocale : 'en'
  globalThis.localStorage?.setItem(languageStorageKey, selected)
  await i18n.changeLanguage(selected)
}

export { i18n }