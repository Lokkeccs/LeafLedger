import { createBrowserRouter, createMemoryRouter } from 'react-router-dom'
import { AppLayout } from './AppLayout'
import { HomeRoute } from './HomeRoute'
import { NotFoundRoute } from './NotFoundRoute'
import { RouteErrorBoundary } from './RouteErrorBoundary'

export function createAppRouter(initialEntry?: string) {
	const routes = [{ path: '/', element: <AppLayout />, errorElement: <RouteErrorBoundary />, children: [{ index: true, lazy: async () => ({ Component: HomeRoute }) }, { path: 'accounts', lazy: async () => ({ Component: (await import('../features/accounts/AccountsPage')).AccountsPage }) }, { path: '*', Component: NotFoundRoute }] }]
	return initialEntry ? createMemoryRouter(routes, { initialEntries: [initialEntry] }) : createBrowserRouter(routes)
}

export const appRouter = createAppRouter()