# ADR-0003: API contract pipeline — build-time OpenAPI → generated TypeScript client

- **Status:** accepted
- **Date:** 2026-07-06
- **Deciders:** LL Architect (planned P1-WP04), LL Backend Dev + LL Frontend Dev (implemented)
- **Related:** WP P1-WP04 ([plan](../../rebuild/plans/P1-WP04-openapi-client-pipeline.md)); target architecture Part 3 §6 (API design — "OpenAPI is the single contract"), §7 (frontend `api/` = generated client + types); quality Part 5 §22/§40 (PR pipeline includes a "contract diff (OpenAPI)" gate; SemVer `/api/v1`)

## Context
Part 3 mandates that **OpenAPI is the single contract** between backend and frontend and that the
frontend's `api/` layer is *generated*, never hand-edited — the mechanism that "kills payload drift
permanently" (the class of bug where client and server disagree about a shape). P1-WP04 had to choose
*how* the contract is produced, generated, and enforced, before any real endpoint exists. Several axes
needed deciding: where the OpenAPI document comes from (build-time artifact vs a running server), what
generates the TypeScript, whether to also generate data-fetching hooks, and how CI prevents drift.

## Decision
We will operate a **single-contract pipeline** with a committed OpenAPI artifact and a CI drift gate:

1. **Backend emits the contract at build time.** `Microsoft.AspNetCore.OpenApi` (`AddOpenApi` /
   `MapOpenApi`) plus the build-time generator `Microsoft.Extensions.ApiDescription.Server` write a
   deterministic `backend/openapi/leafledger-v1.json` on `dotnet build` — **headless, no running server
   or Postgres required**. That JSON is **committed** as the canonical contract. (The generator has no
   output-filename knob, so an MSBuild `Move` target renames its default output to the canonical name.)
2. **Frontend generates types + a typed fetch client, not hooks.** `openapi-typescript` generates
   `app/src/api/schema.d.ts` (types) and `openapi-fetch` provides a tiny typed runtime client
   (`app/src/api/client.ts`, a stable committed bootstrap). Data-fetching **hooks are written by hand in
   the application layer** — they are deliberately *not* code-generated into `src/api`.
3. **CI enforces a contract-diff gate.** A `contract` job (in `pr.yml` and `main.yml`) rebuilds the
   backend (re-emits the JSON), runs `npm run gen:api`, and fails on any diff in `backend/openapi/**` or
   `app/src/api/**`. Determinism is pinned (exact tool versions; LF via `.gitattributes`) so the
   byte-diff gate cannot flake; the gate is proven red (a schema change without regeneration fails).

## Consequences
- **Positive:** Payload drift is structurally impossible to merge — a stale contract or stale client
  fails CI. Types flow from one source; misuse of a field is a compile error.
- **Positive:** The pipeline needs no running server/database to produce or verify the contract, so it
  is cheap and reliable in CI.
- **Positive (boundary):** Keeping generated output to *types + a thin client* preserves the
  `features → application → api` architecture — the query layer (TanStack Query) stays hand-written in
  the application layer where it belongs, not generated into the `api/` boundary.
- **Neutral:** One intentional hand-authored file lives in `src/api` (`client.ts`, base-URL bootstrap);
  everything else there is generated. This is documented so the "never edit generated files" rule is
  not misread as forbidding it.
- **Negative / accepted deviations:** the filename-rename MSBuild target and an `app/.npmrc`
  `legacy-peer-deps=true` (the generator's peer `typescript@^5` vs the repo's `typescript@6`) are small
  pieces of glue the pipeline depends on.

## Alternatives considered
- **Generate TanStack Query hooks (e.g. `orval`).** Rejected: it would place the data-fetching/query
  layer inside the `src/api` boundary, violating the module architecture; hooks stay hand-written in the
  application layer.
- **Serve OpenAPI from a running app and generate against the live endpoint.** Rejected: requires a
  booted server (and often a database) in CI, is slower and flakier, and yields no committed artifact to
  diff against — defeating the drift gate.
- **Hand-maintain TypeScript types mirroring the API.** Rejected: reintroduces exactly the drift class
  the contract pipeline exists to eliminate.
- **A formal API-versioning library (`Asp.Versioning.*`) now.** Deferred: `/api/v1` is a literal route
  prefix for today; SemVer + deprecation windows (Part 5 §40) are a later WP.
