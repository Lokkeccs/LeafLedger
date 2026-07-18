import { useQuery } from '@tanstack/react-query'
import { getBalanceSheet } from '../reports'
import { qk } from './queryKeys'

export function useBalanceSheet(spaceId: string) {
  return useQuery({ queryKey: qk.reports.balanceSheet(spaceId), queryFn: () => getBalanceSheet(spaceId) })
}