import { isRouteErrorResponse, Link, useRouteError } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

interface ProblemDetails { title?: string; detail?: string; code?: string }

function problemDetails(error: unknown): ProblemDetails | undefined {
  if (typeof error !== 'object' || error === null) return undefined
  const candidate = error as ProblemDetails
  return candidate.title || candidate.detail || candidate.code ? candidate : undefined
}

export function RouteErrorBoundary() {
  const { t } = useTranslation()
  const error = useRouteError()
  const details = problemDetails(error)
  const status = isRouteErrorResponse(error) ? error.status : undefined
  const message = details?.detail ?? details?.title ?? (status === 404 ? t('shell.routeNotFoundDetail') : t('shell.routeErrorDetail'))

  return <main className="error-shell" role="alert"><p className="eyebrow">{t('shell.routeErrorEyebrow')}{status ? ` ${status}` : ''}</p><h1>{t('shell.routeErrorTitle')}</h1><p>{message}</p>{details?.code && <code>{details.code}</code>}<div className="error-actions"><button type="button" onClick={() => window.location.reload()}>{t('shell.retry')}</button><Link className="button-link" to="/">{t('shell.goHome')}</Link></div></main>
}