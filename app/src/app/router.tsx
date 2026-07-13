import { createBrowserRouter } from 'react-router-dom'
import { AppLayout } from './AppLayout'
import { HomeRoute } from './HomeRoute'
import { NotFoundRoute } from './NotFoundRoute'
import { RouteErrorBoundary } from './RouteErrorBoundary'

export const appRouter = createBrowserRouter([{ path: '/', element: <AppLayout />, errorElement: <RouteErrorBoundary />, children: [{ index: true, lazy: async () => ({ Component: HomeRoute }) }, { path: '*', Component: NotFoundRoute }] }])