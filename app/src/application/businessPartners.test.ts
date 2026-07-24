import type { ApiClient } from '../api/client'
import { describe, expect, it, vi } from 'vitest'
import { createBusinessPartner, createBusinessPartnerSubmission, deleteBusinessPartner, updateBusinessPartner } from './businessPartners'

const partner = { id: 'bp-1', name: 'Acme', partnerNumber: 'P-1', type: 'customer', countryCode: 'CH', isActive: true, validFrom: null, validTo: null, notes: null, version: '7' }
const input = { name: 'Acme', type: 'customer', isActive: true, validFrom: null, validTo: null, partnerNumber: 'P-1', countryCode: 'CH', notes: null }

describe('business partner application wrappers', () => {
  it('sends an idempotency key on create', async () => {
    const POST = vi.fn().mockResolvedValue({ data: partner, response: { status: 201 } })
    const client = { POST } as unknown as ApiClient
    const submission = createBusinessPartnerSubmission(input)
    await createBusinessPartner('space-1', submission, client)
    expect(POST).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/partners', expect.objectContaining({ params: { path: { spaceId: 'space-1' }, header: { 'Idempotency-Key': submission.idempotencyKey } }, body: input }))
  })

  it('passes the version through on update', async () => {
    const PATCH = vi.fn().mockResolvedValue({ data: partner, response: { status: 200 } })
    const client = { PATCH } as unknown as ApiClient
    const update = { ...input, version: '7' }
    await updateBusinessPartner('space-1', 'bp-1', { input: update, idempotencyKey: '01HHOOK' }, client)
    expect(PATCH).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/partners/{partnerId}', expect.objectContaining({ params: { path: { spaceId: 'space-1', partnerId: 'bp-1' }, header: { 'Idempotency-Key': '01HHOOK' } }, body: update }))
  })

  it('accepts a successful no-content delete', async () => {
    const DELETE = vi.fn().mockResolvedValue({ error: undefined, response: { status: 204 } })
    await expect(deleteBusinessPartner('space-1', 'bp-1', '01HHOOK', { DELETE } as unknown as ApiClient)).resolves.toBeUndefined()
  })
})