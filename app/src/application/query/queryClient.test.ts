import { describe, expect, it } from 'vitest'
import { createQueryClient } from './queryClient'

describe('query client conventions', () => {
  const httpError = (status: number) => Object.assign(new Error(`HTTP ${status}`), { status })

  it('uses explicit cache and focus defaults', () => {
    const options = createQueryClient().getDefaultOptions()
    expect(options.queries?.staleTime).toBe(30_000)
    expect(options.queries?.gcTime).toBe(5 * 60_000)
    expect(options.queries?.refetchOnWindowFocus).toBe(false)
    expect(options.queries?.throwOnError).toBe(true)
    expect(options.mutations?.retry).toBe(false)
  })

  it('does not retry client errors but bounds transient failures', () => {
    const retry = createQueryClient().getDefaultOptions().queries?.retry
    expect(typeof retry).toBe('function')
    if (typeof retry !== 'function') return
    expect(retry(0, httpError(422))).toBe(false)
    expect(retry(0, httpError(503))).toBe(true)
    expect(retry(1, new Error('network'))).toBe(true)
    expect(retry(2, new Error('network'))).toBe(false)
  })
})