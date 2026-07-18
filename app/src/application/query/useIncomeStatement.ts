import { useQuery } from '@tanstack/react-query'
import { getIncomeStatement } from '../reports'
import { qk } from './queryKeys'

export function useIncomeStatement(spaceId: string) {
  return useQuery({ queryKey: qk.reports.incomeStatement(spaceId), queryFn: () => getIncomeStatement(spaceId) })
}