import { apiClient, type ApiClient } from '../api/client'
import type { components } from '../api/schema'
import { newIdempotencyKey } from './idempotencyKey'

export type BusinessPartner = components['schemas']['BusinessPartnerView']
export type BusinessPartnerCommand = components['schemas']['CreateBusinessPartnerCommand']
export type BusinessPartnerUpdate = components['schemas']['UpdateBusinessPartnerCommand']
export type BusinessPartnerSubmission<T> = { input: T; idempotencyKey: string }

export class BusinessPartnerManagementError extends Error {
  readonly status: number
  readonly issues: Array<{ code: string; message: string; field?: string }>

  constructor(status: number, issues: Array<{ code: string; message: string; field?: string }>) {
    super(issues.map((issue) => issue.message).join('; ') || 'Business partner request failed')
    this.name = 'BusinessPartnerManagementError'
    this.status = status
    this.issues = issues
  }
}

export function mapBusinessPartnerIssues(error: unknown): Array<{ code: string; message: string; field?: string }> {
  if (typeof error !== 'object' || error === null) return []
  const candidate = error as { issues?: Array<{ code: string; message: string; field?: string | null }>; errors?: Array<{ code: string; message: string; field?: string | null }>; detail?: string | null; code?: string }
  const issues = candidate.issues ?? candidate.errors
  if (issues?.length) return issues.map((issue) => ({ code: issue.code, message: issue.message, ...(issue.field ? { field: issue.field } : {}) }))
  if (candidate.detail) return [{ code: 'request.failed', message: candidate.detail }]
  return candidate.code ? [{ code: candidate.code, message: candidate.code }] : []
}

export function createBusinessPartnerSubmission(input: BusinessPartnerCommand): BusinessPartnerSubmission<BusinessPartnerCommand> {
  return { input, idempotencyKey: newIdempotencyKey() }
}

export function createBusinessPartnerUpdateSubmission(input: BusinessPartnerUpdate): BusinessPartnerSubmission<BusinessPartnerUpdate> {
  return { input, idempotencyKey: newIdempotencyKey() }
}

export async function getBusinessPartners(spaceId: string, client: ApiClient = apiClient): Promise<BusinessPartner[]> {
  const { data } = await client.GET('/api/v1/spaces/{spaceId}/partners', { params: { path: { spaceId } } })
  if (data === undefined) throw new Error('Failed to fetch business partners')
  return data.partners.map((partner) => ({ ...partner }))
}

export async function getBusinessPartner(spaceId: string, partnerId: string, client: ApiClient = apiClient): Promise<BusinessPartner> {
  const { data, error, response } = await client.GET('/api/v1/spaces/{spaceId}/partners/{partnerId}', { params: { path: { spaceId, partnerId } } })
  if (data) return { ...data }
  throw new BusinessPartnerManagementError(response.status, mapBusinessPartnerIssues(error))
}

export async function createBusinessPartner(spaceId: string, submission: BusinessPartnerSubmission<BusinessPartnerCommand>, client: ApiClient = apiClient): Promise<BusinessPartner> {
  const { data, error, response } = await client.POST('/api/v1/spaces/{spaceId}/partners', { params: { path: { spaceId }, header: { 'Idempotency-Key': submission.idempotencyKey } }, body: submission.input })
  if (data) return { ...data }
  throw new BusinessPartnerManagementError(response.status, mapBusinessPartnerIssues(error))
}

export async function updateBusinessPartner(spaceId: string, partnerId: string, submission: BusinessPartnerSubmission<BusinessPartnerUpdate>, client: ApiClient = apiClient): Promise<BusinessPartner> {
  const { data, error, response } = await client.PATCH('/api/v1/spaces/{spaceId}/partners/{partnerId}', { params: { path: { spaceId, partnerId }, header: { 'Idempotency-Key': submission.idempotencyKey } }, body: submission.input })
  if (data) return { ...data }
  throw new BusinessPartnerManagementError(response.status, mapBusinessPartnerIssues(error))
}

export async function deleteBusinessPartner(spaceId: string, partnerId: string, idempotencyKey = newIdempotencyKey(), client: ApiClient = apiClient): Promise<void> {
  const { error, response } = await client.DELETE('/api/v1/spaces/{spaceId}/partners/{partnerId}', { params: { path: { spaceId, partnerId }, header: { 'Idempotency-Key': idempotencyKey } } })
  if (response.status === 204) return
  throw new BusinessPartnerManagementError(response.status, mapBusinessPartnerIssues(error))
}