import { useMutation, useQueryClient } from '@tanstack/react-query'
import { postJournalEntry, type JournalEntrySubmission } from '../journalEntries'
import { qk } from './queryKeys'

export function usePostJournalEntry(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (submission: JournalEntrySubmission) => postJournalEntry(spaceId, submission.input, undefined, submission.idempotencyKey),
    throwOnError: false,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: qk.journalEntries.list(spaceId) }),
        queryClient.invalidateQueries({ queryKey: qk.reports.trialBalance(spaceId) }),
        queryClient.invalidateQueries({ queryKey: qk.reports.balanceSheet(spaceId) }),
        queryClient.invalidateQueries({ queryKey: qk.reports.incomeStatement(spaceId) }),
      ])
    },
  })
}