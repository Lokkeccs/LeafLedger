import { isRouteErrorResponse, Link, useRouteError } from 'react-router-dom'

interface ProblemDetails { title?: string; detail?: string; code?: string }

function problemDetails(error: unknown): ProblemDetails | undefined {
  if (typeof error !== 'object' || error === null) return undefined
  const candidate = error as ProblemDetails
  return candidate.title || candidate.detail || candidate.code ? candidate : undefined
}

export function RouteErrorBoundary() {
  const error = useRouteError()
  const details = problemDetails(error)
  const status = isRouteErrorResponse(error) ? error.status : undefined
  const message = details?.detail ?? details?.title ?? (status === 404 ? 'The requested page was not found.' : 'This route could not be loaded.')

  return <main className="error-shell" role="alert"><p className="eyebrow">Route error{status ? ` ${status}` : ''}</p><h1>We could not open this view</h1><p>{message}</p>{details?.code && <code>{details.code}</code>}<div className="error-actions"><button type="button" onClick={() => window.location.reload()}>Retry</button><Link className="button-link" to="/">Go home</Link></div></main>
}