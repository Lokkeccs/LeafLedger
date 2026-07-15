import { apiClient, type ApiClient } from '../api/client'

export interface TrialBalanceRow {
  accountId: string
  accountCode: number
  accountName: string
  accountKind: string
  baseBalanceMinor: number
}

export interface TrialBalance {
  spaceId: string
  rows: TrialBalanceRow[]
  totalBaseBalanceMinor: number
}

export async function getTrialBalance(spaceId: string, client: ApiClient = apiClient): Promise<TrialBalance> {
  const { data } = await client.GET('/api/v1/spaces/{spaceId}/reports/trial-balance', {
    params: { path: { spaceId } },
  })
  if (data === undefined) throw new Error('Failed to fetch trial balance')
  return {
    spaceId: data.spaceId,
    rows: data.lines.map((row) => ({ ...row })),
    totalBaseBalanceMinor: data.totalBaseBalanceMinor,
  }
}