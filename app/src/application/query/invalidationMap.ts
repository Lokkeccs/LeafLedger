import type { QueryKey } from '@tanstack/react-query'
import { qk } from './queryKeys'

export type InvalidationTopic = 'reports.trialBalance' | 'journalEntries.list'

export const invalidationMap: Record<InvalidationTopic, (spaceId: string) => QueryKey[]> = {
  'reports.trialBalance': (spaceId) => [
    qk.reports.trialBalance(spaceId),
    qk.reports.balanceSheet(spaceId),
    qk.reports.incomeStatement(spaceId),
  ],
  'journalEntries.list': (spaceId) => [qk.journalEntries.list(spaceId)],
}

export function queryKeysForTopic(topic: string, spaceId: string): QueryKey[] {
  if (!(topic in invalidationMap)) return []
  return invalidationMap[topic as InvalidationTopic](spaceId)
}