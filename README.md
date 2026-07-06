# LeafLedger

Greenfield rebuild of the LeafLedger accounting system: React 19 PWA frontend, .NET 9 modular-monolith API, PostgreSQL.

## Layout

| Path | Contents |
|---|---|
| `app/` | Frontend (Vite + React 19 + TS strict) |
| `backend/` | .NET solution (SharedKernel, Host, tests) |
| `docs/architecture/rebuild/` | Authoritative specs (parts 01–07) |
| `docs/rebuild/` | Work-package plans + [status tracker](docs/rebuild/status.md) |
| `tools/` | Repo scripts (page-budget gate) |
| `tests/fixtures/golden/` | Golden fixtures pinning old-system behavior |
| `help/` | User help content (placeholder) |

## Getting started

- Frontend: `cd app && npm ci && npm run dev` (checks: `npm run lint && npm run typecheck && npm test`)
- Backend: `cd backend && dotnet test`

## Local development

### Prerequisites
- **Docker Desktop** (or Docker engine) installed and running.

### One-command startup

```bash
docker compose up -d --build
```

This starts two services:
- `db` (PostgreSQL 17): listens on `localhost:5432`
- `api` (.NET Host): listens on `localhost:8080`

### Health checks

Once the stack is running, verify connectivity:

```bash
# Liveness probe (process only, always 200 when running)
curl -f http://localhost:8080/health

# Readiness probe (includes DB connection, 200 when DB is reachable)
curl -f http://localhost:8080/health/ready
```

When the API starts, it depends on the `db` service reaching healthy status. If the readiness probe returns **503**, the database is still starting; wait a few seconds and retry.

### Configuration

Default credentials are defined in `.env.example`. Create a `.env` file in the repo root to override:

```bash
cp .env.example .env
# Edit .env as needed; then docker compose will use it
docker compose up -d --build
```

Port overrides (if 5432 or 8080 are in use on your machine):

```bash
DB_PORT=5433 API_PORT=8081 docker compose up -d --build
```

### Cleanup

```bash
# Stop services and remove containers
docker compose down

# Also remove the named volume (data persists by default)
docker compose down -v
```

The old system (`Lokkeccs/Accounting`) is read-only reference material — see the [rebuild playbook](docs/architecture/rebuild/07-vibe-coding-playbook.md).
