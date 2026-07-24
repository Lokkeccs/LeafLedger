import { useMutation, useQueryClient } from '@tanstack/react-query'
import { importGroupsCsv } from '../accountImport'
import { queryKeysForTopic } from './invalidationMap'

export function useImportGroups(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (csv: string) => importGroupsCsv(spaceId, csv),
    throwOnError: false,
    onSuccess: async () => Promise.all([
      ...queryKeysForTopic('accountGroups.import', spaceId).map((queryKey) => queryClient.invalidateQueries({ queryKey })),
    ]),
  })
}