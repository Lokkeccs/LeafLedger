import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { balanceMirror, currencyPolicyAllows } from './balanceMirror'

type CurrencyPolicyFixture = { input: { accounts: { id: number; currency: string; type: string }[]; references: { accountId: number; txCurrency: string }[] }; expected: { ok?: boolean } }
const fixtureNames = ['cp-asset-match', 'cp-asset-mismatch', 'cp-case-insensitive', 'cp-empty-accountcurrency-skips', 'cp-empty-txcurrency-skips', 'cp-equity-mismatch', 'cp-expense-any', 'cp-income-any', 'cp-liability-mismatch', 'cp-missing-account-skips', 'cp-multi-issue']

function loadFixture(name: string): CurrencyPolicyFixture {
  const path = new URL(`../../../../tests/fixtures/golden/ledger-core/currency-policy/${name}.json`, import.meta.url)
  return JSON.parse(readFileSync(path, 'utf8')) as CurrencyPolicyFixture
}

describe('balanceMirror', () => {
  it.each(fixtureNames)('matches currency-policy fixture %s', (name) => {
    const fixture = loadFixture(name)
    const accounts = fixture.input.accounts.map((account) => ({ id: String(account.id), currency: account.currency, kind: account.type }))
    const allowed = fixture.input.references.every((reference) => currencyPolicyAllows(accounts.find((account) => account.id === String(reference.accountId)), reference.txCurrency))
    expect(allowed).toBe(fixture.expected.ok === true)
  })

  it('uses exact integer balance arithmetic and UX guards', () => {
    const accounts = [{ id: '1', currency: 'CHF', kind: 'asset' }]
    expect(balanceMirror('Entry', [{ accountId: '1', currency: 'CHF', amountMinor: 100 }, { accountId: '1', currency: 'CHF', amountMinor: -100 }], accounts)).toEqual([])
    expect(balanceMirror('Entry', [{ accountId: '1', currency: 'CHF', amountMinor: 101 }, { accountId: '1', currency: 'CHF', amountMinor: -100 }], accounts)).toContainEqual({ code: 'entry.unbalanced', line: null })
    expect(balanceMirror('', [{ accountId: '1', currency: 'CHF', amountMinor: 0 }], accounts).map((issue) => issue.code)).toEqual(expect.arrayContaining(['request.invalid.description', 'request.invalid.lines']))
  })
})