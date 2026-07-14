// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import de from './locales/de.json'
import en from './locales/en.json'
import { queryClient } from '../application/query/queryClient'
import { i18n, languageStorageKey, setLanguage } from './index'

describe('i18n runtime', () => {
  beforeEach(async () => {
    localStorage.clear()
    await setLanguage('en')
  })

  it('loads both launch locales and falls back to English', async () => {
    expect(i18n.t('common.save')).toBe('Save')
    await setLanguage('de')
    expect(i18n.t('common.save')).toBe('Speichern')
    await setLanguage('unknown')
    expect(i18n.language).toBe('en')
    expect(i18n.t('common.save')).toBe('Save')
  })

  it('persists the selected language as device preference', async () => {
    await setLanguage('de')
    expect(localStorage.getItem(languageStorageKey)).toBe('de')
  })

  it('keeps the preference when sign-out clears the query cache', async () => {
    await setLanguage('de')
    queryClient.clear()
    expect(localStorage.getItem(languageStorageKey)).toBe('de')
  })

  it('has the active shell keys in both locale objects', () => {
    expect(en).toMatchObject({ common: { save: expect.any(String) }, nav: expect.any(Object), auth: expect.any(Object), shell: expect.any(Object) })
    expect(de).toMatchObject({
      nav: { primary: expect.any(String), overview: expect.any(String), workspace: expect.any(String), online: expect.any(String) },
      auth: { notConfigured: expect.any(String) },
      shell: Object.fromEntries(Object.keys(en.shell).map((key) => [key, expect.any(String)])),
    })
  })
})