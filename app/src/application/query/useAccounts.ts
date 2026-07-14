import { useQuery } from '@tanstack/react-query'
import { getAccounts } from '../accounts'
import { qk } from './queryKeys'

export function useAccounts(spaceId: string) {
  return useQuery({ queryKey: qk.accounts.list(spaceId), queryFn: () => getAccounts(spaceId) })
}