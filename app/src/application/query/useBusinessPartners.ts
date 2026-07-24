import { useQuery } from '@tanstack/react-query'
import { getBusinessPartners } from '../businessPartners'
import { qk } from './queryKeys'

export function useBusinessPartners(spaceId: string) {
  return useQuery({ queryKey: qk.businessPartners.list(spaceId), queryFn: () => getBusinessPartners(spaceId) })
}