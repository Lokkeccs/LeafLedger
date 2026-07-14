import { apiClient, type ApiClient } from '../api/client'
import type { components } from '../api/schema'
import { newIdempotencyKey } from './idempotencyKey'

export type JournalEntryLineInput = { accountId: string; amountMinor: number; currency: string }
export type JournalEntryInput = { date: string; description: string; reference: string | null; lines: JournalEntryLineInput[] }
export type PostedEntry = Pick<components['schemas']['PostingResponse'], 'id' | 'entryNo' | 'date'>
export type PostingIssue = { code: string; message: string; line: number | null }

export class PostingError extends Error {
  readonly status: number
  readonly issues: PostingIssue[]
  constructor(status: number, issues: PostingIssue[]) {
    super(issues.map((issue) => issue.message).join('; ') || 'Journal entry could not be posted')
    this.name = 'PostingError'
    this.status = status
    this.issues = issues
  }
}

export type JournalEntrySubmission = { input: JournalEntryInput; idempotencyKey: string }

export function createJournalEntrySubmission(input: JournalEntryInput): JournalEntrySubmission {
  return { input, idempotencyKey: newIdempotencyKey() }
}

function mapIssues(error: unknown): PostingIssue[] {
  if (typeof error !== 'object' || error === null) return []
  const candidate = error as { issues?: components['schemas']['LedgerProblemIssue'][] | null; errors?: components['schemas']['LedgerProblemError'][] | null; detail?: string | null }
  const issues = candidate.issues ?? candidate.errors
  if (issues?.length) return issues.map((issue) => ({ code: issue.code, message: issue.message, line: issue.line }))
  return candidate.detail ? [{ code: 'request.failed', message: candidate.detail, line: null }] : []
}

export async function postJournalEntry(spaceId: string, input: JournalEntryInput, client: ApiClient = apiClient, idempotencyKey = newIdempotencyKey()): Promise<PostedEntry> {
  const { data, error, response } = await client.POST('/api/v1/spaces/{spaceId}/journal-entries', {
    params: { path: { spaceId }, header: { 'Idempotency-Key': idempotencyKey } },
    body: {
      date: input.date, description: input.description, reference: input.reference,
      lines: input.lines.map((line) => ({ accountId: line.accountId, amountMinor: line.amountMinor, currency: line.currency, baseAmountMinor: line.amountMinor, fxRate: null, vatCodeId: null, businessPartnerId: null, projectId: null, attributions: null })),
    },
  })
  if (data) return { id: data.id, entryNo: data.entryNo, date: data.date }
  throw new PostingError(response.status, mapIssues(error))
}