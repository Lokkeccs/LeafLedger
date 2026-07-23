import { describe, expect, it, vi } from 'vitest'
import type { ApiClient } from '../api/client'
import { AccountManagementError, createAccount, createAccountGroup, createAccountSubmission, createGroupSubmission, getAccountGroups, getAccounts, mapAccountManagementIssues, setAccountActive, updateAccount, updateAccountGroup } from './accounts'

function fakeClient(result: unknown): ApiClient {
  return { GET: vi.fn().mockResolvedValue(result) } as unknown as ApiClient
}

describe('getAccounts', () => {
  it('maps the generated account catalog response', async () => {
    const account = {
      id: 'acc-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', isActive: true,
      groupId: 'group-1', validFrom: null, validTo: null, fxPolicy: null,
    }
    const client = fakeClient({ data: { spaceId: 'space-1', accounts: [account] } })

    await expect(getAccounts('space-1', client)).resolves.toEqual([account])
    expect(client.GET).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/accounts', { params: { path: { spaceId: 'space-1' } } })
  })

  it('throws when the generated client returns no data', async () => {
    await expect(getAccounts('space-1', fakeClient({ error: {} }))).rejects.toThrow('Failed to fetch accounts')
  })
})

const account = { id: 'acc-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', isActive: true, groupId: 'group-1', validFrom: null, validTo: null, fxPolicy: null }
const group = { id: 'group-1', name: 'Assets', rangeStart: 1000, rangeEnd: 1999, parentId: null, fxPolicy: null }

describe('account management wrappers', () => {
  it('sends idempotency keys and maps account/group responses', async () => {
    const createInput = { groupId: 'group-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', isActive: true, validFrom: null, validTo: null, fxPolicy: null }
    const updateInput = { groupId: 'group-1', code: 1000, name: 'Cash', currency: 'CHF', kind: 'Asset', validFrom: null, validTo: null, fxPolicy: null }
    const createGroupInput = { name: 'Assets', rangeStart: 1000, rangeEnd: 1999, parentId: null, fxPolicy: null }
    const client = {
      POST: vi.fn()
        .mockResolvedValueOnce({ data: account, response: { status: 201 } })
        .mockResolvedValueOnce({ data: account, response: { status: 200 } })
        .mockResolvedValueOnce({ data: group, response: { status: 201 } }),
      PATCH: vi.fn()
        .mockResolvedValueOnce({ data: account, response: { status: 200 } })
        .mockResolvedValueOnce({ data: group, response: { status: 200 } }),
    } as unknown as ApiClient

    const createSubmission = createAccountSubmission(createInput)
    const updateSubmission = { input: updateInput, idempotencyKey: '01HUPDATE' }
    const createGroupSubmissionValue = createGroupSubmission(createGroupInput)
    await expect(createAccount('space-1', createSubmission, client)).resolves.toEqual(account)
    await expect(updateAccount('space-1', 'acc-1', updateSubmission, client)).resolves.toEqual(account)
    await expect(setAccountActive('space-1', 'acc-1', false, '01HACTIVE', client)).resolves.toEqual(account)
    await expect(createAccountGroup('space-1', createGroupSubmissionValue, client)).resolves.toEqual(group)
    await expect(updateAccountGroup('space-1', 'group-1', { input: createGroupInput, idempotencyKey: '01HGROUP' }, client)).resolves.toEqual(group)

    const postCalls = (client.POST as ReturnType<typeof vi.fn>).mock.calls
    expect(postCalls[0]).toEqual(['/api/v1/spaces/{spaceId}/accounts', { params: { path: { spaceId: 'space-1' }, header: { 'Idempotency-Key': createSubmission.idempotencyKey } }, body: createInput }])
    expect(postCalls[1]).toEqual(['/api/v1/spaces/{spaceId}/accounts/{accountId}/deactivate', { params: { path: { spaceId: 'space-1', accountId: 'acc-1' }, header: { 'Idempotency-Key': '01HACTIVE' } } }])
    expect(postCalls[2]).toEqual(['/api/v1/spaces/{spaceId}/groups', { params: { path: { spaceId: 'space-1' }, header: { 'Idempotency-Key': createGroupSubmissionValue.idempotencyKey } }, body: createGroupInput }])
    const patchCalls = (client.PATCH as ReturnType<typeof vi.fn>).mock.calls
    expect(patchCalls[0]).toEqual(['/api/v1/spaces/{spaceId}/accounts/{accountId}', { params: { path: { spaceId: 'space-1', accountId: 'acc-1' }, header: { 'Idempotency-Key': '01HUPDATE' } }, body: updateInput }])
    expect(patchCalls[1]).toEqual(['/api/v1/spaces/{spaceId}/groups/{groupId}', { params: { path: { spaceId: 'space-1', groupId: 'group-1' }, header: { 'Idempotency-Key': '01HGROUP' } }, body: createGroupInput }])
  })

  it('maps the generated groups read response', async () => {
    const client = { GET: vi.fn().mockResolvedValue({ data: { groups: [group] } }) } as unknown as ApiClient
    await expect(getAccountGroups('space-1', client)).resolves.toEqual([group])
    expect(client.GET).toHaveBeenCalledWith('/api/v1/spaces/{spaceId}/groups', { params: { path: { spaceId: 'space-1' } } })
  })

  it('maps runtime field issues and preserves status for server failures', async () => {
    expect(mapAccountManagementIssues({ issues: [{ code: 'account.code_taken', message: 'Already used', field: 'code' }] })).toEqual([{ code: 'account.code_taken', message: 'Already used', field: 'code' }])
    const client = { POST: vi.fn().mockResolvedValue({ error: { issues: [{ code: 'account.code_taken', message: 'Already used', field: 'code' }] }, response: { status: 422 } }) } as unknown as ApiClient
    await expect(createAccount('space-1', { input: {} as never, idempotencyKey: '01HFAIL' }, client)).rejects.toEqual(expect.objectContaining({ status: 422, issues: [{ code: 'account.code_taken', message: 'Already used', field: 'code' }] } satisfies Partial<AccountManagementError>))
  })
})