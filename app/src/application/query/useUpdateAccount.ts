import { useMutation, useQueryClient } from '@tanstack/react-query'
import { updateAccount, type AccountUpdate, type MutationSubmission } from '../accounts'
import { qk } from './queryKeys'

export function useUpdateAccount(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ accountId, submission }: { accountId: string; submission: MutationSubmission<AccountUpdate> }) => updateAccount(spaceId, accountId, submission),
    throwOnError: false,
    onSuccess: async () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.accounts.list(spaceId) }),
      queryClient.invalidateQueries({ queryKey: qk.reports.trialBalance(spaceId) }),
      queryClient.invalidateQueries({ queryKey: qk.reports.balanceSheet(spaceId) }),
      queryClient.invalidateQueries({ queryKey: qk.reports.incomeStatement(spaceId) }),
      queryClient.invalidateQueries({ queryKey: qk.reports.dashboard(spaceId) }),
      queryClient.invalidateQueries({ queryKey: ['reports', 'accountLedger', spaceId] }),
    ]),
  })
}