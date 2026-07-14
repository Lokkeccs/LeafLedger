// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { render, screen } from '@testing-library/react'
import { Component, type ReactNode } from 'react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getMeta } from '../application/meta'
import { createQueryClient } from '../application/query/queryClient'
import { AppErrorBoundary } from './AppErrorBoundary'
import { AppLayout } from './AppLayout'
import { HomeRoute } from './HomeRoute'
import { NotFoundRoute } from './NotFoundRoute'
import { RouteErrorBoundary } from './RouteErrorBoundary'
import { i18n } from '../i18n'

vi.mock('../application/meta', () => ({ getMeta: vi.fn() }))

const mockedGetMeta = vi.mocked(getMeta)

function QueryRoot({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={createQueryClient()}>{children}</QueryClientProvider>
}

function renderRouter(initialEntry: string, routeElement: ReactNode = <HomeRoute />) {
  const router = createMemoryRouter([
    {
      path: '/',
      element: <AppLayout />,
      errorElement: <RouteErrorBoundary />,
      children: [
        { index: true, element: routeElement },
        { path: '*', element: <NotFoundRoute /> },
      ],
    },
  ], { initialEntries: [initialEntry] })

  return render(<I18nextProvider i18n={i18n}><QueryRoot><RouterProvider router={router} /></QueryRoot></I18nextProvider>)
}

describe('app shell', () => {
  beforeEach(() => {
    mockedGetMeta.mockReset()
  })

  it('renders metadata through the query provider and routes unknown paths to 404', async () => {
    mockedGetMeta.mockResolvedValue({ name: 'Test Ledger', version: 'v-test' })
    renderRouter('/')
    expect(await screen.findByRole('heading', { name: 'Test Ledger' })).toBeTruthy()
    expect(screen.getByText('v-test')).toBeTruthy()

    renderRouter('/missing')
    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeTruthy()
  })

  it('renders the desktop layout with a focusable navigation rail and content pane', () => {
    mockedGetMeta.mockResolvedValue({ name: 'Test Ledger', version: 'v-test' })
    renderRouter('/')
    expect(screen.getByRole('complementary', { name: 'Primary navigation' }).getAttribute('tabindex')).toBe('0')
    expect(screen.getByRole('main').getAttribute('tabindex')).toBe('-1')
    expect(screen.getByRole('link', { name: 'Overview' })).toBeTruthy()
    expect(screen.getByText('Sign-in not configured')).toBeTruthy()
  })

  it('shows the route fallback without exposing an error stack', async () => {
    class ThrowingRoute extends Component {
      render(): never { throw new Error('secret stack detail') }
    }

    renderRouter('/', <ThrowingRoute />)
    expect(await screen.findByRole('heading', { name: 'We could not open this view' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Go home' })).toBeTruthy()
    expect(screen.queryByText('secret stack detail')).toBeNull()
  })

  it('surfaces safe ProblemDetails fields from a route error', async () => {
    class ApiErrorRoute extends Component {
      render(): never {
        throw Object.assign(new Error('internal stack detail'), {
          code: 'auth.identity_unresolved',
          detail: 'The signed-in identity could not be resolved.',
          status: 403,
        })
      }
    }

    renderRouter('/', <ApiErrorRoute />)
    expect(await screen.findByText('The signed-in identity could not be resolved.')).toBeTruthy()
    expect(screen.getByText('auth.identity_unresolved')).toBeTruthy()
    expect(screen.queryByText('internal stack detail')).toBeNull()
  })

  it('renders the top-level fallback and calls the error log seam', () => {
    const onError = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    class ThrowingChild extends Component {
      render(): never { throw new Error('provider failure') }
    }

    render(<AppErrorBoundary onError={onError}><ThrowingChild /></AppErrorBoundary>)
    expect(screen.getByRole('heading', { name: 'Something went wrong' })).toBeTruthy()
    expect(screen.queryByText('provider failure')).toBeNull()
    expect(onError).toHaveBeenCalledOnce()
    expect(consoleError).toHaveBeenCalledWith('LeafLedger app shell error', expect.any(Error))
    consoleError.mockRestore()
  })
})