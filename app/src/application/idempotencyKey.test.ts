import { describe, expect, it } from 'vitest'
import { newIdempotencyKey } from './idempotencyKey'

describe('newIdempotencyKey', () => {
  it('creates unique Crockford-base32 ULIDs', () => {
    const first = newIdempotencyKey()
    const second = newIdempotencyKey()
    expect(first).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/)
    expect(second).toMatch(/^[0-9A-HJKMNP-TV-Z]{26}$/)
    expect(first).not.toBe(second)
  })
})