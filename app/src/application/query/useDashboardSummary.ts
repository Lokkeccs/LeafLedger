import { useQuery } from '@tanstack/react-query'
import { getDashboardSummary } from '../reports'
import { qk } from './queryKeys'

export function useDashboardSummary(spaceId: string) {
  return useQuery({ queryKey: qk.reports.dashboard(spaceId), queryFn: () => getDashboardSummary(spaceId) })
}