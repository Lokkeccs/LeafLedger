import { NavLink, Outlet } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../application/auth/useAuth'

export function AppLayout() {
  const { t } = useTranslation()
  const { account, error, isConfigured, isSignedIn, signIn, signOut } = useAuth()

  return <div className="app-frame"><aside className="nav-rail" aria-label={t('nav.primary')} tabIndex={0}><div className="brand-mark">LL</div><nav><NavLink to="/" end>{t('nav.overview')}</NavLink><NavLink to="/accounts">{t('nav.accounts')}</NavLink><NavLink to="/journal-entries/new">{t('nav.newJournalEntry')}</NavLink></nav></aside><div className="app-workspace"><header className="top-bar"><span className="top-bar-label">{t('nav.workspace')}</span><span className="status-dot">{t('nav.online')}</span><div className="auth-chrome">{!isConfigured ? <span role="status">{t('auth.notConfigured')}</span> : isSignedIn ? <><span>{account?.name ?? account?.username}</span><button type="button" onClick={() => void signOut()}>{t('auth.signOut')}</button></> : <button type="button" onClick={() => void signIn()}>{t('auth.signInMicrosoft')}</button>}{error ? <span className="auth-error" role="alert">{error}</span> : null}</div></header><main className="content-pane" tabIndex={-1}><Outlet /></main></div></div>
}