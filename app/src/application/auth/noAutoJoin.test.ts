import { describe, expect, it } from 'vitest'
import { useAuth } from './useAuth'

it('does not port client-side membership or space auto-provisioning', () => {
  const source = useAuth.toString()
  expect(source).not.toContain('upsertMicrosoftUser')
  expect(source).not.toContain('createMembership')
  expect(source).not.toContain('provisionSpace')
})

describe('auth source boundary', () => {
  it('keeps auth glue outside the features layer', () => {
    expect(new URL('./useAuth.ts', import.meta.url).pathname).toContain('/application/auth/')
  })
})
