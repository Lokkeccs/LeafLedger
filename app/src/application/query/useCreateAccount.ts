import { useMutation, useQueryClient } from '@tanstack/react-query'
import { createAccount, type AccountCommand, type MutationSubmission } from '../accounts'
import { qk } from './queryKeys'

export function useCreateAccount(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (submission: MutationSubmission<AccountCommand>) => createAccount(spaceId, submission),
    throwOnError: false,
    onSuccess: async () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.accounts.list(spaceId) }),
      queryClient.invalidateQueries({ queryKey: qk.accountGroups.list(spaceId) }),
    ]),
  })
}