import { useMutation, useQueryClient } from '@tanstack/react-query'
import { updateBusinessPartner, type BusinessPartnerSubmission, type BusinessPartnerUpdate } from '../businessPartners'
import { qk } from './queryKeys'

export function useUpdateBusinessPartner(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ partnerId, submission }: { partnerId: string; submission: BusinessPartnerSubmission<BusinessPartnerUpdate> }) => updateBusinessPartner(spaceId, partnerId, submission),
    throwOnError: false,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.businessPartners.list(spaceId) }),
  })
}