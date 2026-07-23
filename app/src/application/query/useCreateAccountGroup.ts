import { useMutation, useQueryClient } from '@tanstack/react-query'
import { createAccountGroup, type GroupCommand, type MutationSubmission } from '../accountGroups'
import { qk } from './queryKeys'

export function useCreateAccountGroup(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (submission: MutationSubmission<GroupCommand>) => createAccountGroup(spaceId, submission),
    throwOnError: false,
    onSuccess: async () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.accountGroups.list(spaceId) }),
      queryClient.invalidateQueries({ queryKey: qk.accounts.list(spaceId) }),
    ]),
  })
}