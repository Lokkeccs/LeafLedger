import { QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import { queryClient } from '../application/query/queryClient'
import { AppErrorBoundary } from './AppErrorBoundary'
import { appRouter } from './router'

export function AppRoot() {
  return <QueryClientProvider client={queryClient}><AppErrorBoundary><RouterProvider router={appRouter} /></AppErrorBoundary></QueryClientProvider>
}