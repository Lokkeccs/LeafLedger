import { useMutation, useQueryClient } from '@tanstack/react-query'
import { importAccountsCsv } from '../accountImport'
import { queryKeysForTopic } from './invalidationMap'

export function useImportAccounts(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (csv: string) => importAccountsCsv(spaceId, csv),
    throwOnError: false,
    onSuccess: async () => Promise.all([
      ...queryKeysForTopic('accounts.import', spaceId).map((queryKey) => queryClient.invalidateQueries({ queryKey })),
    ]),
  })
}