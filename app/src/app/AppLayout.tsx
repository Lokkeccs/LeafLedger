import { NavLink, Outlet } from 'react-router-dom'

export function AppLayout() {
  return <div className="app-frame"><aside className="nav-rail" aria-label="Primary navigation" tabIndex={0}><div className="brand-mark">LL</div><nav><NavLink to="/" end>Overview</NavLink></nav></aside><div className="app-workspace"><header className="top-bar"><span className="top-bar-label">Workspace</span><span className="status-dot">Online</span></header><main className="content-pane" tabIndex={-1}><Outlet /></main></div></div>
}