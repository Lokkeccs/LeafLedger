// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useSpaceInvalidation } from './useSpaceInvalidation'

const signalr = vi.hoisted(() => {
  const connection = {
    handlers: new Map<string, (payload: { spaceId: string; topic: string }) => void>(),
    on: vi.fn((event: string, handler: (payload: { spaceId: string; topic: string }) => void) => { connection.handlers.set(event, handler) }),
    off: vi.fn(),
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
  }
  const builderState = {
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    configureLogging: vi.fn().mockReturnThis(),
  }
  class Builder {
    withUrl = builderState.withUrl
    withAutomaticReconnect = builderState.withAutomaticReconnect
    configureLogging = builderState.configureLogging
    build = vi.fn(() => connection)
  }
  return { Builder, builderState, connection }
})

const auth = vi.hoisted(() => ({ account: { homeAccountId: 'account-1' } }))
const acquireApiToken = vi.hoisted(() => vi.fn().mockResolvedValue('bearer-token'))

vi.mock('@microsoft/signalr', () => ({ HubConnectionBuilder: signalr.Builder, LogLevel: { Warning: 2 } }))
vi.mock('../auth/useAuth', () => ({ useAuth: () => auth }))
vi.mock('../auth/authTokens', () => ({ acquireApiToken }))

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={new QueryClient()}>{children}</QueryClientProvider>
}

describe('useSpaceInvalidation', () => {
  beforeEach(() => {
    signalr.connection.handlers.clear()
    signalr.connection.on.mockClear()
    signalr.connection.off.mockClear()
    signalr.connection.start.mockClear()
    signalr.connection.stop.mockClear()
    acquireApiToken.mockClear()
  })

  it('opens one authenticated space connection and cleans it up', async () => {
    const { unmount } = renderHook(() => useSpaceInvalidation('space one'), { wrapper })
    expect(signalr.connection.start).toHaveBeenCalledOnce()
    expect(signalr.builderState.withUrl).toHaveBeenCalledWith('/hubs/space?spaceId=space%20one', expect.any(Object))
    const options = signalr.builderState.withUrl.mock.calls[0]?.[1] as { accessTokenFactory: () => Promise<string> }
    await expect(options.accessTokenFactory()).resolves.toBe('bearer-token')
    expect(acquireApiToken).toHaveBeenCalledWith(auth.account)
    unmount()
    expect(signalr.connection.stop).toHaveBeenCalledOnce()
  })

  it('invalidates mapped keys once per burst and leaves unmapped keys stable', async () => {
    vi.useFakeTimers()
    const client = new QueryClient()
    const invalidateQueries = vi.spyOn(client, 'invalidateQueries')
    function TestWrapper({ children }: { children: ReactNode }) { return <QueryClientProvider client={client}>{children}</QueryClientProvider> }
    const { unmount } = renderHook(() => useSpaceInvalidation('space-1'), { wrapper: TestWrapper })
    const handler = signalr.connection.handlers.get('spaceInvalidated')
    expect(handler).toBeDefined()

    act(() => {
      handler?.({ spaceId: 'space-1', topic: 'reports.trialBalance' })
      handler?.({ spaceId: 'space-1', topic: 'reports.trialBalance' })
      handler?.({ spaceId: 'space-1', topic: 'accounts.list' })
      handler?.({ spaceId: 'space-2', topic: 'reports.trialBalance' })
      vi.advanceTimersByTime(50)
    })

    expect(invalidateQueries).toHaveBeenCalledTimes(4)
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'trialBalance', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'balanceSheet', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'incomeStatement', 'space-1'] })
    expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['reports', 'accountLedger', 'space-1'] })
    unmount()
    vi.useRealTimers()
  })
})