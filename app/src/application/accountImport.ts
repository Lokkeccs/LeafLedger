import { apiClient, type ApiClient } from '../api/client'
import type { components } from '../api/schema'
import { AccountManagementError, mapAccountManagementIssues } from './accounts'
import { newIdempotencyKey } from './idempotencyKey'

export type ImportReport = components['schemas']['ImportReport']
export type AccountImportRequest = components['schemas']['AccountImportRequest']
export type GroupImportRequest = components['schemas']['GroupImportRequest']

type ImportResponse = { data?: ImportReport | undefined; error?: unknown | undefined; response: Response }

function isImportReport(value: unknown): value is ImportReport {
  if (!value || typeof value !== 'object') return false
  const report = value as Partial<ImportReport>
  return typeof report.total === 'number' && typeof report.created === 'number' && typeof report.updated === 'number' && typeof report.failed === 'number' && Array.isArray(report.rows)
}

function getImportReport(result: ImportResponse): ImportReport | undefined {
  return result.response.status === 422 && isImportReport(result.error) ? result.error : undefined
}

function throwImportError(result: ImportResponse): never {
  throw new AccountManagementError(result.response.status, mapAccountManagementIssues(result.error))
}

export async function exportAccountsCsv(spaceId: string, client: ApiClient = apiClient): Promise<string> {
  const { data, error, response } = await client.GET('/api/v1/spaces/{spaceId}/accounts/export', { params: { path: { spaceId } } })
  if (data !== undefined) return data
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}

export async function exportGroupsCsv(spaceId: string, client: ApiClient = apiClient): Promise<string> {
  const { data, error, response } = await client.GET('/api/v1/spaces/{spaceId}/groups/export', { params: { path: { spaceId } } })
  if (data !== undefined) return data
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}

export async function importAccountsCsv(spaceId: string, csv: string, client: ApiClient = apiClient): Promise<ImportReport> {
  const result = await client.POST('/api/v1/spaces/{spaceId}/accounts/import', {
    params: { path: { spaceId }, header: { 'Idempotency-Key': newIdempotencyKey() } },
    body: csv,
  }) as ImportResponse
  if (result.data !== undefined) return result.data
  const report = getImportReport(result)
  if (report) return report
  throwImportError(result)
}

export async function importGroupsCsv(spaceId: string, csv: string, client: ApiClient = apiClient): Promise<ImportReport> {
  const result = await client.POST('/api/v1/spaces/{spaceId}/groups/import', {
    params: { path: { spaceId }, header: { 'Idempotency-Key': newIdempotencyKey() } },
    body: csv,
  }) as ImportResponse
  if (result.data !== undefined) return result.data
  const report = getImportReport(result)
  if (report) return report
  throwImportError(result)
}

export async function importAccountsRows(spaceId: string, request: AccountImportRequest, client: ApiClient = apiClient): Promise<ImportReport> {
  const { data, error, response } = await client.POST('/api/v1/spaces/{spaceId}/accounts/import', {
    params: { path: { spaceId }, header: { 'Idempotency-Key': newIdempotencyKey() } }, body: request,
  })
  if (data !== undefined) return data
  const report = getImportReport({ data, error, response })
  if (report) return report
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}

export async function importGroupsRows(spaceId: string, request: GroupImportRequest, client: ApiClient = apiClient): Promise<ImportReport> {
  const { data, error, response } = await client.POST('/api/v1/spaces/{spaceId}/groups/import', {
    params: { path: { spaceId }, header: { 'Idempotency-Key': newIdempotencyKey() } }, body: request,
  })
  if (data !== undefined) return data
  const report = getImportReport({ data, error, response })
  if (report) return report
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}