import { useMutation, useQueryClient } from '@tanstack/react-query'
import { updateAccountGroup, type GroupUpdate, type MutationSubmission } from '../accountGroups'
import { qk } from './queryKeys'

export function useUpdateAccountGroup(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ groupId, submission }: { groupId: string; submission: MutationSubmission<GroupUpdate> }) => updateAccountGroup(spaceId, groupId, submission),
    throwOnError: false,
    onSuccess: async () => Promise.all([
      queryClient.invalidateQueries({ queryKey: qk.accountGroups.list(spaceId) }),
      queryClient.invalidateQueries({ queryKey: qk.accounts.list(spaceId) }),
    ]),
  })
}