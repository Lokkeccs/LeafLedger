// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getAccounts } from '../accounts'
import { createQueryClient } from './queryClient'
import { useAccounts } from './useAccounts'

vi.mock('../accounts', () => ({ getAccounts: vi.fn() }))

const mockedGetAccounts = vi.mocked(getAccounts)

function QueryProbe({ spaceId }: { spaceId: string }) {
  useAccounts(spaceId)
  return null
}

describe('useAccounts', () => {
  beforeEach(() => mockedGetAccounts.mockReset())

  it('uses the space-scoped key and getAccounts query function', async () => {
    mockedGetAccounts.mockResolvedValue([])
    const client = createQueryClient()

    render(<QueryClientProvider client={client}><QueryProbe spaceId="space-1" /></QueryClientProvider>)

    await waitFor(() => expect(mockedGetAccounts).toHaveBeenCalledWith('space-1'))
    expect(client.getQueryCache().find({ queryKey: ['accounts', 'list', 'space-1'] })).toBeTruthy()
  })
})