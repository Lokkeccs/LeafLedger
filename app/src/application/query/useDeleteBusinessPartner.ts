import { useMutation, useQueryClient } from '@tanstack/react-query'
import { deleteBusinessPartner } from '../businessPartners'
import { qk } from './queryKeys'

export function useDeleteBusinessPartner(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ partnerId, idempotencyKey }: { partnerId: string; idempotencyKey?: string }) => deleteBusinessPartner(spaceId, partnerId, idempotencyKey),
    throwOnError: false,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.businessPartners.list(spaceId) }),
  })
}