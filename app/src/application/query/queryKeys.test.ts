import { describe, expect, it } from 'vitest'
import { qk } from './queryKeys'

describe('query key factory', () => {
  it('creates stable hierarchical keys', () => {
    expect(qk.meta()).toEqual(['meta'])
    expect(qk.accounts.list('sp_1')).toEqual(['accounts', 'list', 'sp_1'])
    expect(qk.reports.trialBalance('sp_1')).toEqual(['reports', 'trialBalance', 'sp_1'])
  })

  it('keeps module keys distinct', () => {
    expect(qk.accounts.list('sp_1')).not.toEqual(qk.reports.trialBalance('sp_1'))
    expect(qk.accounts.list('sp_1')).not.toEqual(qk.accounts.list('sp_2'))
  })
})