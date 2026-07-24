// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useCreateBusinessPartner } from './useCreateBusinessPartner'
import { useDeleteBusinessPartner } from './useDeleteBusinessPartner'
import { useUpdateBusinessPartner } from './useUpdateBusinessPartner'

const { createBusinessPartner, updateBusinessPartner, deleteBusinessPartner } = vi.hoisted(() => ({ createBusinessPartner: vi.fn().mockResolvedValue({}), updateBusinessPartner: vi.fn().mockResolvedValue({}), deleteBusinessPartner: vi.fn().mockResolvedValue(undefined) }))
vi.mock('../businessPartners', () => ({ createBusinessPartner, updateBusinessPartner, deleteBusinessPartner }))

function wrapper(client: QueryClient) { return function Wrapper({ children }: { children: ReactNode }) { return <QueryClientProvider client={client}>{children}</QueryClientProvider> } }

describe('business partner query hooks', () => {
  beforeEach(() => vi.clearAllMocks())
  it('invalidates the partner list after every mutation', async () => {
    const client = new QueryClient()
    const invalidate = vi.spyOn(client, 'invalidateQueries').mockResolvedValue()
    const hookWrapper = wrapper(client)
    const create = renderHook(() => useCreateBusinessPartner('space-1'), { wrapper: hookWrapper })
    await act(async () => { await create.result.current.mutateAsync({ input: {}, idempotencyKey: '01HHOOK' } as never) })
    const update = renderHook(() => useUpdateBusinessPartner('space-1'), { wrapper: hookWrapper })
    await act(async () => { await update.result.current.mutateAsync({ partnerId: 'bp-1', submission: { input: {}, idempotencyKey: '01HHOOK' } } as never) })
    const remove = renderHook(() => useDeleteBusinessPartner('space-1'), { wrapper: hookWrapper })
    await act(async () => { await remove.result.current.mutateAsync({ partnerId: 'bp-1' }) })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['businessPartners', 'list', 'space-1'] })
    expect(createBusinessPartner).toHaveBeenCalled()
    expect(updateBusinessPartner).toHaveBeenCalled()
    expect(deleteBusinessPartner).toHaveBeenCalled()
  })
})