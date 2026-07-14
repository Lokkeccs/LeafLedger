import { useMeta } from '../application/query/useMeta'
import { useTranslation } from 'react-i18next'

export function HomeRoute() {
  const { t } = useTranslation()
  const meta = useMeta()
  if (meta.isPending || !meta.data) return <p>{t('shell.loading')}</p>
  return <section className="home-route"><p className="eyebrow">{t('shell.overviewEyebrow')}</p><h1>{meta.data.name}</h1><p className="lead">{t('shell.workspaceReady')}</p><dl className="meta-list"><div><dt>{t('shell.apiContract')}</dt><dd>{meta.data.version}</dd></div></dl></section>
}