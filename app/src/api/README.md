# Generated OpenAPI client (P1-WP04)

Data access for the frontend goes **only** through this folder, used via
application-layer hooks (`features → application → api`). Never call `fetch`
directly; never import `src/api` from `src/features`.

## Files
- `schema.d.ts` — **generated**, do not edit. Types produced by
  `openapi-typescript` from the backend contract
  (`backend/openapi/leafledger-v1.json`). Regenerate, never hand-edit.
- `client.ts` — the single stable bootstrap that instantiates the typed
  `openapi-fetch` client over `schema.d.ts`. This is the only hand-authored
  file in the folder (base-URL/auth wiring lands with the P3 app shell).

## Regenerate
```
npm run gen:api
```
CI runs the same command and fails the build on any drift between the committed
contract, the committed `schema.d.ts`, and a fresh regeneration (the OpenAPI
contract-diff gate). This folder is lint-ignored and import-restricted.
