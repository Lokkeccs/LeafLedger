import { useQuery } from '@tanstack/react-query'
import { getAccountLedger } from '../reports'
import { qk } from './queryKeys'

export function useAccountLedger(spaceId: string, accountId: string | undefined, range: { from?: string; to?: string }) {
  return useQuery({
    queryKey: qk.reports.accountLedger(spaceId, accountId ?? '', range.from, range.to),
    queryFn: () => getAccountLedger(spaceId, accountId!, range),
    enabled: accountId !== undefined,
  })
}