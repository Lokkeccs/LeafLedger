import { describe, expect, it, vi } from 'vitest'
import type { ApiClient } from '../api/client'
import { postJournalEntry, PostingError } from './journalEntries'

const testIdempotencyKey = 'synthetic-test-idempotency-key'

const input = { date: '2026-07-14', description: 'Office supplies', reference: null, lines: [{ accountId: 'acc-1', amountMinor: 1250, currency: 'CHF' }, { accountId: 'acc-2', amountMinor: -1250, currency: 'CHF' }] }

function fakeClient(result: unknown): ApiClient {
  return { POST: vi.fn().mockResolvedValue(result) } as unknown as ApiClient
}

describe('postJournalEntry', () => {
  it('maps the same-currency request and preserves the idempotency key', async () => {
    const client = fakeClient({ data: { id: 'je-1', entryNo: 7, date: input.date } })
    await expect(postJournalEntry('space-1', input, client, testIdempotencyKey)).resolves.toEqual({ id: 'je-1', entryNo: 7, date: input.date })
    expect(client.POST).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/journal-entries', expect.objectContaining({ params: expect.objectContaining({ header: { 'Idempotency-Key': testIdempotencyKey } }) }))
    expect(client.POST).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ body: expect.objectContaining({ lines: expect.arrayContaining([expect.objectContaining({ amountMinor: 1250, baseAmountMinor: 1250, fxRate: null }), expect.objectContaining({ amountMinor: -1250, baseAmountMinor: -1250, fxRate: null })]) }) }))
  })

  it('maps structured server issues including line indexes', async () => {
    const client = fakeClient({ response: { status: 422 }, error: { issues: [{ code: 'currency_policy.currency_not_allowed', message: 'Currency is not allowed.', line: 1 }] } })
    await expect(postJournalEntry('space-1', input, client, testIdempotencyKey)).rejects.toEqual(expect.objectContaining({ status: 422, issues: [{ code: 'currency_policy.currency_not_allowed', message: 'Currency is not allowed.', line: 1 }] }))
    await expect(postJournalEntry('space-1', input, fakeClient({ response: { status: 400 }, error: { detail: 'Description is required.' } }), testIdempotencyKey)).rejects.toBeInstanceOf(PostingError)
  })
})