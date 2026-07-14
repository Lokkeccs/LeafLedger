import { MsalProvider } from '@azure/msal-react'
import { QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { initializeMsal, msalInstance } from '../application/auth/msalInstance'
import { installAuthMiddleware } from '../application/auth/authTokens'
import { queryClient } from '../application/query/queryClient'
import { AppErrorBoundary } from './AppErrorBoundary'
import { appRouter } from './router'

export function AppRoot() {
  const [initialized, setInitialized] = useState(false)

  useEffect(() => {
    let mounted = true
    void initializeMsal().then(() => {
      installAuthMiddleware()
      if (mounted) setInitialized(true)
    })
    return () => { mounted = false }
  }, [])

  return <MsalProvider instance={msalInstance}>
    {initialized
      ? <QueryClientProvider client={queryClient}><AppErrorBoundary><RouterProvider router={appRouter} /></AppErrorBoundary></QueryClientProvider>
      : <div className="app-splash" role="status">Preparing secure workspace...</div>}
  </MsalProvider>
}