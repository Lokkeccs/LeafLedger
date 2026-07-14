// @vitest-environment jsdom
import { render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoot } from './providers'

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((resolvePromise) => { resolve = resolvePromise })
  return { promise, resolve }
}

const initialization = vi.hoisted(() => ({ promise: deferred<void>() }))
const installAuthMiddleware = vi.hoisted(() => vi.fn())

vi.mock('@azure/msal-react', () => ({ MsalProvider: ({ children }: { children: ReactNode }) => <div data-testid="msal-provider">{children}</div> }))
vi.mock('../application/auth/msalInstance', () => ({
  msalInstance: {},
  initializeMsal: () => initialization.promise.promise,
}))
vi.mock('../application/auth/authTokens', () => ({ installAuthMiddleware }))
vi.mock('./router', () => ({ appRouter: {} }))
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return { ...actual, RouterProvider: () => <div>Authenticated shell</div> }
})

describe('AppRoot authentication gate', () => {
  beforeEach(() => {
    initialization.promise = deferred<void>()
    installAuthMiddleware.mockClear()
  })

  it('keeps the shell behind MSAL initialization and installs middleware after it resolves', async () => {
    render(<AppRoot />)
    expect(screen.getByRole('status').textContent).toBe('Preparing secure workspace...')
    expect(screen.queryByText('Authenticated shell')).toBeNull()

    initialization.promise.resolve()

    await waitFor(() => expect(screen.getByText('Authenticated shell')).toBeTruthy())
    expect(installAuthMiddleware).toHaveBeenCalledOnce()
  })
})
