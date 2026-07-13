import { useQuery } from '@tanstack/react-query'
import { getMeta } from '../meta'
import { qk } from './queryKeys'

export function useMeta() {
  return useQuery({ queryKey: qk.meta(), queryFn: () => getMeta() })
}