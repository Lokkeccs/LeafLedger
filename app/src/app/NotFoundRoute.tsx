import { Link } from 'react-router-dom'

export function NotFoundRoute() {
  return <section className="home-route"><p className="eyebrow">404</p><h1>Page not found</h1><p className="lead">That workspace view does not exist.</p><Link className="button-link" to="/">Return to overview</Link></section>
}