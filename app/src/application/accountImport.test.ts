import { describe, expect, it, vi } from 'vitest'
import type { ApiClient } from '../api/client'
import { AccountManagementError } from './accounts'
import { exportAccountsCsv, exportGroupsCsv, importAccountsCsv, importAccountsRows, importGroupsCsv } from './accountImport'

const report = { total: 1, created: 0, updated: 0, failed: 1, rows: [{ rowNumber: 2, outcome: 'failed', errors: [{ code: 'account.group_unknown', message: 'Unknown group', field: 'group' }], warnings: [] }] }

function fakeClient(result: unknown): ApiClient {
  return { GET: vi.fn().mockResolvedValue(result), POST: vi.fn().mockResolvedValue(result) } as unknown as ApiClient
}

describe('account import/export wrappers', () => {
  it('returns a row-level report from a CSV 422 response', async () => {
    const client = fakeClient({ error: report, response: { status: 422 } })

    await expect(importAccountsCsv('space-1', 'csv', client)).resolves.toEqual(report)
    await expect(importGroupsCsv('space-1', 'csv', client)).resolves.toEqual(report)
  })

  it('returns a row-level report from a JSON 422 response', async () => {
    const client = fakeClient({ error: report, response: { status: 422 } })

    await expect(importAccountsRows('space-1', { rows: [] }, client)).resolves.toEqual(report)
  })

  it('keeps transport failures typed', async () => {
    const client = fakeClient({ error: { issues: [{ code: 'forbidden', message: 'Forbidden' }] }, response: { status: 403 } })

    await expect(exportAccountsCsv('space-1', client)).rejects.toEqual(expect.objectContaining({ status: 403 }))
    await expect(importAccountsCsv('space-1', 'csv', client)).rejects.toBeInstanceOf(AccountManagementError)
  })

  it('calls both export operations and returns CSV text', async () => {
    const client = {
      GET: vi.fn()
        .mockResolvedValueOnce({ data: 'accounts-csv', response: { status: 200 } })
        .mockResolvedValueOnce({ data: 'groups-csv', response: { status: 200 } }),
    } as unknown as ApiClient

    await expect(exportAccountsCsv('space-1', client)).resolves.toBe('accounts-csv')
    await expect(exportGroupsCsv('space-1', client)).resolves.toBe('groups-csv')
    expect(client.GET).toHaveBeenNthCalledWith(1, '/api/v1/spaces/{spaceId}/accounts/export', { params: { path: { spaceId: 'space-1' } } })
    expect(client.GET).toHaveBeenNthCalledWith(2, '/api/v1/spaces/{spaceId}/groups/export', { params: { path: { spaceId: 'space-1' } } })
  })

  it('posts imports with an idempotency key', async () => {
    const client = fakeClient({ data: { total: 0, created: 0, updated: 0, failed: 0, rows: [] }, response: { status: 200 } })

    await importAccountsCsv('space-1', 'csv', client)
    expect(client.POST).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/accounts/import', expect.objectContaining({
      params: { path: { spaceId: 'space-1' }, header: { 'Idempotency-Key': expect.any(String) } },
      body: 'csv',
    }))
  })
})