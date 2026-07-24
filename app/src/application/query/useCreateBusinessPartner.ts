import { useMutation, useQueryClient } from '@tanstack/react-query'
import { createBusinessPartner, type BusinessPartnerCommand, type BusinessPartnerSubmission } from '../businessPartners'
import { qk } from './queryKeys'

export function useCreateBusinessPartner(spaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (submission: BusinessPartnerSubmission<BusinessPartnerCommand>) => createBusinessPartner(spaceId, submission),
    throwOnError: false,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: qk.businessPartners.list(spaceId) }),
  })
}