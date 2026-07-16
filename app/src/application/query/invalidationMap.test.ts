import { describe, expect, it } from 'vitest'
import { invalidationMap, queryKeysForTopic } from './invalidationMap'

describe('realtime invalidation map', () => {
  it('maps every Phase-3 topic to its exact space query key', () => {
    expect(invalidationMap['reports.trialBalance']('space-1')).toEqual([['reports', 'trialBalance', 'space-1']])
    expect(invalidationMap['journalEntries.list']('space-1')).toEqual([['journalEntries', 'list', 'space-1']])
  })

  it('ignores unknown topics without broad cache invalidation', () => {
    expect(queryKeysForTopic('accounts.list', 'space-1')).toEqual([])
  })
})