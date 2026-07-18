import type { QueryKey } from '@tanstack/react-query'
import { qk } from './queryKeys'

export type InvalidationTopic = 'reports.trialBalance' | 'journalEntries.list' | 'accounts.list' | 'accountGroups.list' | 'accounts.import' | 'accountGroups.import'

export const invalidationMap: Record<InvalidationTopic, (spaceId: string) => QueryKey[]> = {
  'reports.trialBalance': (spaceId) => [
    qk.reports.trialBalance(spaceId),
    qk.reports.balanceSheet(spaceId),
    qk.reports.incomeStatement(spaceId),
    ['reports', 'accountLedger', spaceId],
  ],
  'journalEntries.list': (spaceId) => [qk.journalEntries.list(spaceId)],
  'accounts.list': (spaceId) => [qk.accounts.list(spaceId)],
  'accountGroups.list': (spaceId) => [qk.accountGroups.list(spaceId)],
  'accounts.import': (spaceId) => [qk.accounts.list(spaceId), qk.accountGroups.list(spaceId), ...invalidationMap['reports.trialBalance'](spaceId)],
  'accountGroups.import': (spaceId) => [qk.accounts.list(spaceId), qk.accountGroups.list(spaceId), ...invalidationMap['reports.trialBalance'](spaceId)],
}

export function queryKeysForTopic(topic: string, spaceId: string): QueryKey[] {
  if (!(topic in invalidationMap)) return []
  return invalidationMap[topic as InvalidationTopic](spaceId)
}