import { useQuery } from '@tanstack/react-query'
import { getTrialBalance } from '../reports'
import { qk } from './queryKeys'

export function useTrialBalance(spaceId: string) {
  return useQuery({ queryKey: qk.reports.trialBalance(spaceId), queryFn: () => getTrialBalance(spaceId) })
}