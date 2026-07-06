import { describe, expect, it, vi } from 'vitest'
import { getMeta } from './meta'
import type { ApiClient } from '../api/client'

function fakeClient(result: unknown): ApiClient {
  return { GET: vi.fn().mockResolvedValue(result) } as unknown as ApiClient
}

describe('getMeta', () => {
  it('maps typed metadata from the generated client', async () => {
    const client = fakeClient({ data: { name: 'LeafLedger', version: 'v1' } })

    await expect(getMeta(client)).resolves.toEqual({ name: 'LeafLedger', version: 'v1' })
    expect(client.GET).toHaveBeenCalledWith('/api/v1/meta')
  })

  it('throws when the client returns no data', async () => {
    const client = fakeClient({ error: {} })

    await expect(getMeta(client)).rejects.toThrow('Failed to fetch API metadata')
  })
})
