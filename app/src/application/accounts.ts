import { apiClient, type ApiClient } from '../api/client'
import type { components } from '../api/schema'
import { newIdempotencyKey } from './idempotencyKey'

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

export type AccountGroup = components['schemas']['GroupView']
export type AccountCommand = components['schemas']['CreateAccountCommand']
export type AccountUpdate = components['schemas']['UpdateAccountCommand']
export type GroupCommand = components['schemas']['CreateGroupCommand']
export type GroupUpdate = components['schemas']['UpdateGroupCommand']
export type MutationSubmission<T> = { input: T; idempotencyKey: string }

export class AccountManagementError extends Error {
  readonly status: number
  readonly issues: Array<{ code: string; message: string; field?: string }>

  constructor(status: number, issues: Array<{ code: string; message: string; field?: string }>) {
    super(issues.map((issue) => issue.message).join('; ') || 'Account management request failed')
    this.name = 'AccountManagementError'
    this.status = status
    this.issues = issues
  }
}

export function mapAccountManagementIssues(error: unknown): Array<{ code: string; message: string; field?: string }> {
  if (typeof error !== 'object' || error === null) return []
  const candidate = error as { issues?: Array<{ code: string; message: string; field?: string | null; line?: number | null }>; errors?: Array<{ code: string; message: string; field?: string | null; line?: number | null }>; detail?: string | null; code?: string }
  const issues = candidate.issues ?? candidate.errors
  if (issues?.length) return issues.map((issue) => ({ code: issue.code, message: issue.message, ...(issue.field ? { field: issue.field } : {}) }))
  if (candidate.detail) return [{ code: 'request.failed', message: candidate.detail }]
  return candidate.code ? [{ code: candidate.code, message: candidate.code }] : []
}

export function createAccountSubmission(input: AccountCommand): MutationSubmission<AccountCommand> {
  return { input, idempotencyKey: newIdempotencyKey() }
}

export function createAccountUpdateSubmission(input: AccountUpdate): MutationSubmission<AccountUpdate> {
  return { input, idempotencyKey: newIdempotencyKey() }
}

export function createGroupSubmission(input: GroupCommand): MutationSubmission<GroupCommand> {
  return { input, idempotencyKey: newIdempotencyKey() }
}

export function createGroupUpdateSubmission(input: GroupUpdate): MutationSubmission<GroupUpdate> {
  return { input, idempotencyKey: newIdempotencyKey() }
}

export async function getAccountGroups(spaceId: string, client: ApiClient = apiClient): Promise<AccountGroup[]> {
  const { data } = await client.GET('/api/v1/spaces/{spaceId}/groups', { params: { path: { spaceId } } })
  if (data === undefined) throw new Error('Failed to fetch account groups')
  return data.groups.map((group) => ({ ...group }))
}

export async function createAccount(spaceId: string, submission: MutationSubmission<AccountCommand>, client: ApiClient = apiClient): Promise<Account> {
  const { data, error, response } = await client.POST('/api/v1/spaces/{spaceId}/accounts', {
    params: { path: { spaceId }, header: { 'Idempotency-Key': submission.idempotencyKey } }, body: submission.input,
  })
  if (data) return { ...data }
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}

export async function updateAccount(spaceId: string, accountId: string, submission: MutationSubmission<AccountUpdate>, client: ApiClient = apiClient): Promise<Account> {
  const { data, error, response } = await client.PATCH('/api/v1/spaces/{spaceId}/accounts/{accountId}', {
    params: { path: { spaceId, accountId }, header: { 'Idempotency-Key': submission.idempotencyKey } }, body: submission.input,
  })
  if (data) return { ...data }
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}

export async function setAccountActive(spaceId: string, accountId: string, active: boolean, idempotencyKey = newIdempotencyKey(), client: ApiClient = apiClient): Promise<Account> {
  const path = active ? '/api/v1/spaces/{spaceId}/accounts/{accountId}/activate' : '/api/v1/spaces/{spaceId}/accounts/{accountId}/deactivate'
  const { data, error, response } = await client.POST(path, { params: { path: { spaceId, accountId }, header: { 'Idempotency-Key': idempotencyKey } } })
  if (data) return { ...data }
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}

export async function createAccountGroup(spaceId: string, submission: MutationSubmission<GroupCommand>, client: ApiClient = apiClient): Promise<AccountGroup> {
  const { data, error, response } = await client.POST('/api/v1/spaces/{spaceId}/groups', {
    params: { path: { spaceId }, header: { 'Idempotency-Key': submission.idempotencyKey } }, body: submission.input,
  })
  if (data) return { ...data }
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}

export async function updateAccountGroup(spaceId: string, groupId: string, submission: MutationSubmission<GroupUpdate>, client: ApiClient = apiClient): Promise<AccountGroup> {
  const { data, error, response } = await client.PATCH('/api/v1/spaces/{spaceId}/groups/{groupId}', {
    params: { path: { spaceId, groupId }, header: { 'Idempotency-Key': submission.idempotencyKey } }, body: submission.input,
  })
  if (data) return { ...data }
  throw new AccountManagementError(response.status, mapAccountManagementIssues(error))
}