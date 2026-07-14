import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../application/auth/useAuth'

export function AppLayout() {
  const { account, error, isConfigured, isSignedIn, signIn, signOut } = useAuth()

  return <div className="app-frame"><aside className="nav-rail" aria-label="Primary navigation" tabIndex={0}><div className="brand-mark">LL</div><nav><NavLink to="/" end>Overview</NavLink></nav></aside><div className="app-workspace"><header className="top-bar"><span className="top-bar-label">Workspace</span><span className="status-dot">Online</span><div className="auth-chrome">{!isConfigured ? <span role="status">Sign-in not configured</span> : isSignedIn ? <><span>{account?.name ?? account?.username}</span><button type="button" onClick={() => void signOut()}>Sign out</button></> : <button type="button" onClick={() => void signIn()}>Sign in</button>}{error ? <span className="auth-error" role="alert">{error}</span> : null}</div></header><main className="content-pane" tabIndex={-1}><Outlet /></main></div></div>
}