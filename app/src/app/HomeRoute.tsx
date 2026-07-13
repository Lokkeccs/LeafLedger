import { useMeta } from '../application/query/useMeta'

export function HomeRoute() {
  const meta = useMeta()
  if (meta.isPending || !meta.data) return <p>Loading workspace...</p>
  return <section className="home-route"><p className="eyebrow">Overview</p><h1>{meta.data.name}</h1><p className="lead">Your accounting workspace is ready for the next module.</p><dl className="meta-list"><div><dt>API contract</dt><dd>{meta.data.version}</dd></div></dl></section>
}