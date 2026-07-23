import { useQuery } from '@tanstack/react-query'
import { getAccountGroups } from '../accountGroups'
import { qk } from './queryKeys'

export function useAccountGroups(spaceId: string) {
  return useQuery({ queryKey: qk.accountGroups.list(spaceId), queryFn: () => getAccountGroups(spaceId) })
}