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

The old system (`Lokkeccs/Accounting`) is read-only reference material — see the [rebuild playbook](docs/architecture/rebuild/07-vibe-coding-playbook.md).
