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

export interface ReportLine {
  accountId: string | null
  accountCode: number | null
  name: string
  accountKind: string
  amountMinor: number
  isDerived: boolean
}

export interface BalanceSheet {
  spaceId: string
  lines: ReportLine[]
  currentResultMinor: number
}

export interface IncomeStatement {
  spaceId: string
  lines: ReportLine[]
  netResultMinor: number
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

export async function getBalanceSheet(spaceId: string, client: ApiClient = apiClient): Promise<BalanceSheet> {
  const { data } = await client.GET('/api/v1/spaces/{spaceId}/reports/balance-sheet', {
    params: { path: { spaceId } },
  })
  if (data === undefined) throw new Error('Failed to fetch balance sheet')
  return { spaceId: data.spaceId, lines: data.lines.map((line) => ({ ...line })), currentResultMinor: data.currentResultMinor }
}

export async function getIncomeStatement(spaceId: string, client: ApiClient = apiClient): Promise<IncomeStatement> {
  const { data } = await client.GET('/api/v1/spaces/{spaceId}/reports/income-statement', {
    params: { path: { spaceId } },
  })
  if (data === undefined) throw new Error('Failed to fetch income statement')
  return { spaceId: data.spaceId, lines: data.lines.map((line) => ({ ...line })), netResultMinor: data.netResultMinor }
}