import { describe, expect, it } from 'vitest'
import { invalidationMap, queryKeysForTopic } from './invalidationMap'

describe('realtime invalidation map', () => {
  it('maps report changes to every affected report query key', () => {
    expect(invalidationMap['reports.trialBalance']('space-1')).toEqual([
      ['reports', 'trialBalance', 'space-1'],
      ['reports', 'balanceSheet', 'space-1'],
      ['reports', 'incomeStatement', 'space-1'],
      ['reports', 'dashboard', 'space-1'],
      ['reports', 'accountLedger', 'space-1'],
    ])
    expect(invalidationMap['journalEntries.list']('space-1')).toEqual([['journalEntries', 'list', 'space-1']])
  })

  it('ignores unknown topics without broad cache invalidation', () => {
    expect(queryKeysForTopic('accounts.list', 'space-1')).toEqual([['accounts', 'list', 'space-1']])
    expect(queryKeysForTopic('accountGroups.list', 'space-1')).toEqual([['accountGroups', 'list', 'space-1']])
    expect(queryKeysForTopic('unknown.topic', 'space-1')).toEqual([])
  })
})