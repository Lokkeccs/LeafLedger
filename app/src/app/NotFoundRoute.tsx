import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'

export function NotFoundRoute() {
  const { t } = useTranslation()
  return <section className="home-route"><p className="eyebrow">404</p><h1>{t('shell.notFoundTitle')}</h1><p className="lead">{t('shell.notFoundDetail')}</p><Link className="button-link" to="/">{t('shell.returnToOverview')}</Link></section>
}