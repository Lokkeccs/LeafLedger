import { MsalProvider } from '@azure/msal-react'
import { I18nextProvider } from 'react-i18next'
import { QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { initializeMsal, msalInstance } from '../application/auth/msalInstance'
import { installAuthMiddleware } from '../application/auth/authTokens'
import { queryClient } from '../application/query/queryClient'
import { AppErrorBoundary } from './AppErrorBoundary'
import { appRouter } from './router'
import { i18n } from '../i18n'
import { ThemeProvider } from './theme/ThemeProvider'

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

  return <I18nextProvider i18n={i18n}><ThemeProvider><MsalProvider instance={msalInstance}>
    {initialized
      ? <QueryClientProvider client={queryClient}><AppErrorBoundary><RouterProvider router={appRouter} /></AppErrorBoundary></QueryClientProvider>
        : <div className="app-splash" role="status">{i18n.t('shell.preparing')}</div>}
      </MsalProvider></ThemeProvider></I18nextProvider>
}