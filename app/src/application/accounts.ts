import { apiClient, type ApiClient } from '../api/client'

export interface Account {
  id: string
  code: number
  name: string
  currency: string
  kind: string
  isActive: boolean
  groupId: string
  validFrom: string | null
  validTo: string | null
  fxPolicy: string | null
}

export async function getAccounts(spaceId: string, client: ApiClient = apiClient): Promise<Account[]> {
  const { data } = await client.GET('/api/v1/spaces/{spaceId}/accounts', {
    params: { path: { spaceId } },
  })
  if (data === undefined) throw new Error('Failed to fetch accounts')
  return data.accounts.map((account) => ({ ...account }))
}