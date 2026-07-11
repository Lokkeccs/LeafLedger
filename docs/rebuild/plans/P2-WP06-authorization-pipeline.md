# P2-WP06 — Authorization pipeline: space role (typed) → module permission endpoint filter; principal-bound RLS binding; license-entitlement seam

- **Phase:** 2 (ledger core)
- **State:** verify — implementation complete; awaiting QA review. No accounting decision required (authorization is access control, not accounting behavior → no golden fixtures, no LL Accounting Expert consult).
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP02 (schema: `memberships` table with typed `role`, `spaces`, RLS `leafledger_app` role + `app.current_space_id`/`app.current_actor` GUCs), P2-WP05 (the two write endpoints this WP protects + the txn-local tenancy binding this WP generalizes).
- **Blocks:** P2-WP08 (property suite may drive the authenticated path), production exposure of the WP05 endpoints (still additionally gated by WP09 idempotency).
- **Estimated size:** ≤ 2 days (authenticated-principal seam + typed role/permission model + authorization endpoint filter on the two WP05 endpoints + principal-bound RLS binding replacing the trusted `X-Actor-Id` header + per-endpoint 401/403 tests + license-entitlement seam + contract regen).

## Re-scope note (LL Architect, 2026-07-11)

The Phase-2 tracker row for WP06 read *"scope → license entitlement → space role (typed) → module permission; RLS session binding."* Delivered literally, that spans **three module contexts that do not yet exist** (Licensing entitlements table, Spaces & Identity identity-links table, full Entra `common` token validation with JWKS/issuer-pattern hardening) — well beyond the ≤ 2-day / independently-verifiable rule. Phase 2 needs the **authorization decision + tenancy binding** on the ledger write path, not the full identity platform. WP06 is therefore scoped to the verifiable authorization core, with the absent stages provided as **tested seams** and carved out into follow-up WPs (status.md updated in the same change):

- **P2-WP06 (this plan)** — authenticated-principal seam (configured JWT bearer + a test authentication scheme for integration tests), typed `SpaceRole` (Owner/Admin/Member/Viewer), a server-side role→module-permission map, an authorization endpoint filter (`RequireSpacePermission("ledger.post"/"ledger.reverse")`) on the two WP05 endpoints, and **principal-bound RLS binding** (actor = the authenticated subject, space = the route id verified against membership) replacing the trusted `X-Actor-Id` header. The **license-entitlement stage is a called seam** (`ILicenseEntitlement`, allow-all default) invoked before role resolution so the spec ordering is honored and testable.
- **P2-WP11 (new, proposed)** — **AuthN hardening:** real Entra `common` JWT validation (signature via JWKS, audience allowlist, issuer-pattern allowlist per §8.1), the **identity-links** table (Entra subject ↔ internal `user_id`), and sign-out/state handling. WP06 assumes (documented) that the token subject GUID *is* the internal `user_id`; WP11 replaces that with the identity-links indirection.
- **License-entitlement real enforcement** — owned by the **Licensing module** (Part 3 §3; roadmap M0 "server-side license enforcement: plans free/pro/bundle, device binding, entitlement middleware"). WP06 only defines and calls the seam; the real check lands with the Licensing module and swaps out the allow-all default. Recorded as a carry-forward, **not** a new Phase-2 ledger-core WP.

WP06 **protects** the WP05 endpoints and **generalizes** WP05's txn-local binding into the authenticated request pipeline; it does not add posting behavior, idempotency, period writes, or reporting.

- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §8 (**Security model**: 1. AuthN Entra `common`; **2. AuthZ pipeline — scope → license entitlement → space role (typed) → module permission (`ledger.post`, `period.close`, `members.manage`) — endpoint filters, tested per endpoint**; 3. Tenancy — RLS as the second wall behind application checks), §2 (verdict table: "Hand-rolled HS256 license JWT + client-side gating" → **Replace** with server-side entitlement middleware + licensing table; "Roles as magic strings, client-side rights tables" → **Replace** with typed roles Owner/Admin/Member/Viewer + per-module permissions enforced in endpoint filters; client mirrors server truth), §3 (Spaces & Identity owns memberships/roles; Licensing owns plans/entitlements — middleware-enforced), §5 (backend structure: Host owns `Program.cs`, DI, **middleware pipeline**; NetArchTest boundary rules; EF confined to `Infrastructure`), §9 (Result-based errors → ProblemDetails; alert on RLS violations); `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 (AuthZ pipeline item 2; Tenancy item 3 — RLS second wall) + §5 (salvage vs rewrite: "Replace" items are spec-derived rewrites pinned by tests); `docs/architecture/rebuild/06-feature-roadmap.md` (M0 server-side license enforcement; RLS + audit; member management UI is M2); ADR-0001 (online-first, server/DB are the source of truth); ADR-0002 (uuid id storage).

## Goal

Make the WP05 ledger write path **authorized**, not merely tenancy-bound. Replace the trusted `X-Actor-Id` header and unauthenticated route `spaceId` with an authenticated principal that is (a) checked for a valid access scope, (b) passed through a license-entitlement seam, (c) resolved to a **typed space role** via the caller's `memberships` row in the target space, and (d) checked for the required **module permission** (`ledger.post` to post, `ledger.reverse` to reverse) in an endpoint filter — returning **401** when unauthenticated, **403** when authenticated but not entitled / not a member / lacking the permission. The authenticated subject then drives the **txn-local RLS binding** (`app.current_actor` = subject, `app.current_space_id` = the membership-verified route space), generalizing WP05's binding into the authenticated pipeline and closing the `N-WP06-pool` and "unauthenticated in Phase 2" carry-forwards.

RLS remains the **second wall** behind these application checks (spec §8.3): even if the filter were bypassed, the space-bound policies still prevent cross-tenant reads/writes.

## Scope

1. **Authenticated-principal seam (Host):**
   - Add `AddAuthentication().AddJwtBearer(...)` with `TokenValidationParameters` driven by configuration (authority, **audience allowlist**, issuer validation) shaped per §8.1 for Entra `common`. **No live-IdP dependency in this WP:** the parameter shape (audience/issuer allowlist, `RequireSignedTokens`, no `ValidateIssuer=false` shortcut) is unit-verified; end-to-end Entra token acquisition, JWKS/key rotation, and issuer-pattern hardening are **P2-WP11**.
   - Integration tests authenticate via a **test authentication scheme** (`WebApplicationFactory` + a `TestAuthHandler`) that injects a `ClaimsPrincipal` carrying a stable subject GUID (`oid`/`sub`) and a `scope` claim — the standard ASP.NET pattern that makes authorization independently verifiable without a live IdP.
   - A scoped `ICurrentUser` abstraction exposes the resolved subject GUID + scopes to the pipeline. **Phase-2 simplification (documented):** the subject GUID *is* the internal `memberships.user_id`; the identity-links indirection is WP11.
2. **Typed roles + permission model (Host, cross-cutting):**
   - `SpaceRole` enum: `Owner`, `Admin`, `Member`, `Viewer` (spec §8.2). A total, explicit parser maps the `memberships.role` string → `SpaceRole`; an unrecognized/blank role is a **deny** (403), never a silent grant.
   - A static, server-side `ModulePermission` set (`ledger.post`, `ledger.reverse`; extensible — `period.close`, `members.manage` reserved for their WPs) and a `role → permissions` map: `Owner`/`Admin` → all; `Member` → `ledger.post` + `ledger.reverse`; `Viewer` → none. No client-supplied rights table (the OLD Dexie `roleRights` model is not ported).
3. **Authorization endpoint filter (Host):**
   - `RequireSpacePermission(permission)` — an `IEndpointFilter` that: verifies authentication (else 401); runs the license-entitlement seam (else 403); reads the caller's role in the route `{spaceId}` via the Ledger module's public membership query (else 403 — not a member); maps role → permissions; and requires `permission` (else 403). On success it stashes the resolved `{spaceId, subject, role}` in `HttpContext.Items` for the posting service to bind.
   - Applied to the WP05 endpoints: `POST …/journal-entries` requires `ledger.post`; `POST …/journal-entries/{id}/reverse` requires `ledger.reverse`.
4. **Membership/role read (Ledger module public contract):**
   - Add `ISpaceMembershipQuery.GetRoleAsync(Guid spaceId, Guid userId, CancellationToken)` → nullable role string, implemented in Ledger `Infrastructure` (EF stays in `Infrastructure`). The read runs under `leafledger_app` with the route `spaceId` bound **txn-locally**, then filters by `userId`; **no membership ⇒ null ⇒ 403**. Exposed via DI in `LedgerModule` so the Host consumes it EF-free.
5. **Principal-bound RLS binding (Ledger Application/Infrastructure):**
   - The posting/reversal transaction binds `app.current_actor` = the **authenticated subject** (from `ICurrentUser`, via the command), not the `X-Actor-Id` header, and `app.current_space_id` = the **membership-verified** route space, both `SET LOCAL` / `is_local => true` (WP05 already does txn-local binding; WP06 changes only the *source* of the actor). The trusted `X-Actor-Id` header path is **removed**.
6. **ProblemDetails for auth failures (Host):**
   - 401 (`Unauthorized`) and 403 (`Forbidden`) return `application/problem+json` consistent with the existing ledger error shape, with stable machine `code`s (`auth.unauthenticated`, `auth.forbidden` / `auth.not_a_member` / `auth.permission_denied` / `auth.license_inactive`). No stack traces; no principal PII in the body.
7. **License-entitlement seam (Host):**
   - `ILicenseEntitlement.IsEntitledAsync(subject, spaceId, permission, ct)` invoked in the filter **before** role resolution (spec order scope→license→role→permission). Default `AllowAllLicenseEntitlement` returns entitled; a test double proves the ordering (a denying stub yields 403 `auth.license_inactive` **before** any membership read). Real implementation is the Licensing module (carry-forward).
8. **Contract + tests:** regenerate `backend/openapi/leafledger-v1.json` + the TS client (bearer security scheme + 401/403 responses on the two endpoints); the CI `contract` gate must stay green. Update the WP05 HTTP tests to authenticate via the test scheme and drop the `X-Actor-Id` header; add authorization integration tests.

### Ordering (pipeline)

Fixed, documented order matching spec §8.2 and asserted by tests: **authenticated? (401)** → **valid scope? (403 `auth.forbidden`)** → **license entitled? (403 `auth.license_inactive`)** → **member of space? (403 `auth.not_a_member`)** → **role grants permission? (403 `auth.permission_denied`)** → endpoint body (WP05 validity/period/currency/balance → 422 as before). Authorization precedes all WP05 domain validation (a non-member never reaches balance checks).

## Non-goals (explicitly deferred)

- **No real Entra token validation E2E — P2-WP11.** No JWKS/key-rotation client, no live issuer-pattern allowlist hardening beyond config shape, no MSAL/sign-out frontend glue, no **identity-links** table. WP06 assumes subject GUID == `user_id` and substitutes a test auth scheme in integration tests.
- **No Licensing module / entitlements schema.** No plans/entitlements/devices tables, no plan→feature mapping, no HS256 replacement. WP06 provides and calls `ILicenseEntitlement` with an allow-all default only; the real check is the Licensing module's WP.
- **No role/member management endpoints or UI — M2.** No invite/assign/change-role/CRUD, no `RolesPage` port, no `members.manage` endpoint (the permission constant may be reserved but is unused). Memberships are seeded directly in tests (as WP02/WP05 fixtures do).
- **No new business tables / migration.** WP06 reads the existing `memberships`; it adds **no migration**. If a genuinely missing column surfaces, that is a plan amendment, not an ad-hoc migration.
- **No rate limiting.** Spec §6 rate-limiting middleware is a separate concern, not this WP.
- **No change to posting/reversal domain or validity/period/currency/balance behavior.** WP04 domain and WP05 orchestration are unchanged except the actor source; the two balance walls, ProblemDetails 422 codes, and `entry_no` allocation are untouched.
- **No `GET`/reporting/authorization on read endpoints beyond what exists.** Reporting endpoints are WP07 and will reuse this filter then.
- **No `period.close`/`members.manage` enforcement.** Those permissions may exist as reserved constants but are wired by WP10 (period lifecycle) and the Spaces/Identity WP respectively.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (not salvaged as code) — both OLD mechanisms are "Replace" per spec §2
- **Client-side authorization** — `src/auth/rights.ts` (`DEFAULT_ROLE_KEYS = ['Admin','Accountant','Manager']`, `DEFAULT_ROLE_RIGHTS`, `NAVIGATION_RIGHTS`), `src/auth/AuthContext.tsx` (`RoleProvider`/`useRole`/`hasRight`, `isSolo` bypass), `src/features/admin/RolesPage.tsx`, Dexie `roles`/`roleRights` tables. **Rewritten** as server-enforced typed roles + endpoint filters; the OLD roles and the client rights table are **not** ported (target roles are Owner/Admin/Member/Viewer, enforced server-side). Used only to confirm the *vocabulary* of a role→permission model.
- **Device-based licensing** — `backend/LeafLedger.Api/Licensing/CosmosLicenseService.cs`, `Controllers/LicenseController.cs`, `backend/functions/license/index.ts` (hand-rolled HS256 `signJWT`), `src/licensing/client.ts` (plans `free|pro|bundle`, device binding, client caching). **Replaced** by the `ILicenseEntitlement` per-request seam (real impl = Licensing module). WP06 ports **no** licensing code; it only fixes the seam's *position* in the pipeline (before role resolution).
- **Sync scope gating** — `backend/LeafLedger.Auth/AuthServiceExtensions.cs` (`HasScope`/`HasRole`, `"SyncReadWrite"` policy). The OLD gate was a coarse device-sync scope, not per-space authorization. WP06 keeps only the *idea* of a scope claim check; per-space role/permission is new.

### Rewrite (spec-derived; no OLD oracle)
- The typed `SpaceRole`, the role→module-permission map, the `RequireSpacePermission` endpoint filter, the license-entitlement seam, and the principal-bound RLS binding are greenfield per §8. They are pinned by **per-endpoint integration tests** (401/403/allow, ordering, RLS second wall), which is the correct instrument for access-control behavior — not golden fixtures.

## Accounting decisions

**None.** Authorization is access control, not accounting behavior. No golden fixtures, no LL Accounting Expert consult. (Recorded explicitly so QA does not expect an accounting artifact.)

## Golden fixtures

**None required.** WP06 changes no accounting rule and produces no financial output. Access-control behavior is pinned by per-endpoint integration tests (the testing-pyramid tier appropriate for authorization), consistent with the "Replace" verdict being a spec-derived rewrite.

## Dependencies

- No new production NuGet is expected beyond the ASP.NET Core `Microsoft.AspNetCore.Authentication.JwtBearer` package (net9, part of the shared framework via `Microsoft.AspNetCore.App`) — confirm it needs no separate PackageReference; if a version-pinned package is required it is recorded here before use. Test project already has `Microsoft.AspNetCore.Mvc.Testing`/`Microsoft.AspNetCore.TestHost` (WP05).
- The OpenAPI build-time doc + `openapi-typescript`/`openapi-fetch` pipeline (P1-WP04) regenerates the contract (security scheme + 401/403); the CI `contract` diff gate must stay green.

## File list (implementation target)

**New — `backend/src/LeafLedger.Host/Authorization/`** (cross-cutting middleware pipeline lives in the Host per §5)
- `SpaceRole.cs` (enum + total string parser) and `ModulePermissions.cs` (permission constants + `role → permissions` map).
- `RequireSpacePermissionFilter.cs` (`IEndpointFilter` + `RequireSpacePermission(permission)` extension): authn → scope → license seam → membership/role → permission; ProblemDetails 401/403 with stable codes.
- `ICurrentUser.cs` + `HttpContextCurrentUser.cs` (resolve subject GUID + scopes from `HttpContext.User`).
- `ILicenseEntitlement.cs` + `AllowAllLicenseEntitlement.cs` (default seam).

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/`**
- `ISpaceMembershipQuery.cs` (public contract) + `SpaceMembershipQuery.cs` (EF read under txn-local space binding; registered in `LedgerModule.AddLedgerModule`).

**Modified**
- `backend/src/LeafLedger.Host/Program.cs` — add authentication/authorization services + the JWT bearer options (config-driven), register `ICurrentUser`/`ILicenseEntitlement`, apply `RequireSpacePermission("ledger.post"/"ledger.reverse")` to the two ledger endpoints, and wire the auth-failure ProblemDetails.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerEndpoints.cs` — remove the `X-Actor-Id` `TryGetActor` path; take the actor/space from the resolved principal (`ICurrentUser` / `HttpContext.Items`); attach the `RequireSpacePermission` filter (or accept it applied in `Program.cs`).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/JournalPostingService.cs` — bind `app.current_actor` from the command's authenticated subject (already txn-local); no behavior change beyond actor source.
- `backend/src/LeafLedger.Modules.Ledger/Application/Posting/PostingContracts.cs` — command `ActorId` is populated from the authenticated subject (documented), not a request header.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — register `ISpaceMembershipQuery`.
- `backend/src/LeafLedger.Host/appsettings*.json` — auth configuration keys (authority/audience allowlist); **no secrets committed** (placeholders / env-bound).
- `backend/openapi/leafledger-v1.json` — regenerated (bearer security scheme + 401/403 on the two endpoints).
- `app/src/api/schema.d.ts` (+ `client.ts` if the generator changes it) — regenerated; **do not hand-edit**.
- `backend/tests/LeafLedger.IntegrationTests/Ledger/LedgerHttpEndpointTests.cs` — authenticate via the test scheme, drop `X-Actor-Id`; existing 201/422/404/400 assertions updated to the authenticated path.
- `backend/tests/LeafLedger.ArchitectureTests/ModuleBoundaryTests.cs` — only if a new namespace needs coverage; the existing EF-confinement and Domain-purity rules must stay green unchanged.
- `docs/rebuild/plans/P2-WP06-authorization-pipeline.md` + `docs/rebuild/status.md` — notes/state.

**New — tests**
- `backend/tests/LeafLedger.Host.Tests/` **or** unit tests colocated with the Host test project (create if absent) — unit tests for `SpaceRole` parsing (every string case + unknown → deny), the role→permission map, the JWT `TokenValidationParameters` shape (audience allowlist present, issuer validated, signed tokens required), and the filter ordering with in-memory fakes (`ILicenseEntitlement` deny → 403 before membership read).
- `backend/tests/LeafLedger.IntegrationTests/Ledger/LedgerAuthorizationTests.cs` (`[Trait("Category","Integration")]`) — real HTTP + RLS: unauthenticated → 401; authenticated non-member → 403 `auth.not_a_member`; authenticated `Viewer` → 403 `auth.permission_denied` on post; `Member`/`Admin`/`Owner` → 201; license-seam deny → 403 `auth.license_inactive` before membership lookup; actor bound into RLS equals the subject (verified via the persisted `created_by`/audit `app.current_actor`); a caller authorized for space A cannot post/read space B (RLS second wall) even with a valid principal; reversal requires `ledger.reverse`.

No files under `app/src/**` except regenerated `api/**`; no new migration; no `*.Domain` change; no Licensing/identity-links schema.

## Boundary note

- Authorization primitives live in the **Host** (`Program.cs`, DI, middleware pipeline per §5). The Host may reference module public contracts and ASP.NET Core; the arch test `EfCoreIsConfinedToInfrastructureNamespaces` must stay green (the membership read's EF lives in Ledger `Infrastructure`, exposed via `ISpaceMembershipQuery`).
- `*.Domain` stays pure (SharedKernel only) — no auth types leak into Domain; the `DomainNamespacesDependOnlyOnSharedKernel` test stays green.
- Future extraction of memberships/roles into a dedicated `LeafLedger.Modules.Spaces` module (spec §3) is a **carry-forward**, not this WP; memberships currently live in `LedgerDbContext` per D1 (single-context baseline), so the query is exposed from the Ledger module for now.

## Implementation sequence

1. Add `SpaceRole` + parser and `ModulePermissions` map; unit-test parsing (all cases + unknown → deny) and the map.
2. Add `ICurrentUser` + JWT bearer options (config-driven) + a `TestAuthHandler`; unit-test the `TokenValidationParameters` shape.
3. Add `ILicenseEntitlement` + allow-all default; add the `RequireSpacePermission` filter; unit-test ordering with fakes (license-deny short-circuits before membership).
4. Add `ISpaceMembershipQuery` in Ledger `Infrastructure` (txn-local space-bound read) + DI registration.
5. Wire authentication/authorization in `Program.cs`; apply the filter to the two endpoints; switch the posting actor source from `X-Actor-Id` to the authenticated subject; regenerate the OpenAPI contract + TS client.
6. Update the WP05 HTTP tests to the authenticated path; add `LedgerAuthorizationTests` (401/403/allow, ordering, RLS second wall, actor binding).
7. Run Release build + arch tests + full suite (incl. integration under Docker) + lint/typecheck/page-budget + contract gate; document results/deviations; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Build, boundaries & contract:** `dotnet build LeafLedger.sln -c Release` = 0/0; architecture tests stay green (`*.Domain` → SharedKernel only; EF confined to `Infrastructure`; no auth type in Domain); the CI `contract` gate is green after regenerating `leafledger-v1.json` + the TS client (bearer security scheme + 401/403 added, no unintended drift).
2. **Unauthenticated → 401:** `POST …/journal-entries` (and `/reverse`) with **no** bearer token returns **401** `application/problem+json` with code `auth.unauthenticated`; no DB read/write occurs.
3. **Non-member → 403:** an authenticated principal with **no `memberships` row** in `{spaceId}` returns **403** `auth.not_a_member`; the request never reaches WP05 domain validation.
4. **Role→permission enforcement:** a `Viewer` posting returns **403** `auth.permission_denied`; a `Member`, `Admin`, and `Owner` each post successfully (**201**); reversal requires `ledger.reverse` and a `Viewer` is denied while `Member`/`Admin`/`Owner` succeed. Unknown/blank role string → **403** (deny, never grant).
5. **Pipeline ordering:** a denying `ILicenseEntitlement` yields **403** `auth.license_inactive` **before** any membership lookup (verified by a fake that records call order); an unauthenticated request is 401 before the license seam; a non-member is 403 before role→permission. Ordering matches §8.2 (authn → scope → license → member → permission).
6. **Principal-bound RLS binding:** a successful post binds `app.current_actor` = the **authenticated subject GUID** (not any header) and `app.current_space_id` = the route space, both txn-local (`is_local => true`); the persisted `created_by` / audit `actor` equals the subject; the removed `X-Actor-Id` header has no effect (supplying it does not change the actor).
7. **RLS second wall with a valid principal:** a principal that is `Owner` of space A cannot post to or read space B (no membership in B) — 403 at the filter; and even a crafted call that reached persistence with A's context cannot touch B's rows (space-bound policy) — mirroring the WP02 `OpenAppNoContextAsync` fail-closed guarantee; a pooled connection with no context remains fail-closed.
8. **WP05 behavior preserved:** with a valid `Member`+ principal, the existing WP05 outcomes are unchanged — happy-path 201, unbalanced → 422 `journal_entry.unbalanced` (both walls), closed/locked/no-period → 422, currency-policy → 422, reversal 201 linking `reverses_entry_id`, missing target → 404. Authorization only adds a 401/403 gate in front; it does not alter 422/404 codes or the two balance walls.
9. **License seam is a seam, not an implementation:** `ILicenseEntitlement` default is allow-all; no plans/entitlements/devices table exists; the interface is invoked per request in the documented position; the real check is explicitly deferred to the Licensing module (asserted by the absence of any licensing schema/migration in the diff).
10. **ProblemDetails shape:** all auth failures return `application/problem+json` with stable machine `code`s (`auth.unauthenticated` → 401; `auth.forbidden`/`auth.not_a_member`/`auth.permission_denied`/`auth.license_inactive` → 403) and no principal PII / stack traces; domain failures keep their WP05 422/404 shapes.
11. **Scope containment:** `git diff --name-only` is limited to the file list; **no new migration**, no `*.Domain` change, no Licensing/identity-links schema, no role-management endpoints, no rate limiting, no frontend beyond regenerated `api/**`; the existing Ledger integration suite (incl. `HasPendingModelChanges()`) stays green (schema untouched).
12. **Quality gate:** lint, typecheck, page budgets, arch/boundary tests, and the relevant unit + authorization integration tests all pass in Release; integration tests run under Docker (`postgres:17`) via the `Category=Integration` filter and are green on the main-branch job.

## Definition of done

All 12 ACs pass; the two WP05 endpoints require a valid bearer token, verify space membership and the module permission, and bind RLS from the authenticated subject; unauthenticated → 401 and unauthorized → 403 with structured ProblemDetails; RLS proven as the independent second wall with a valid principal; the license-entitlement seam is called in the correct order with an allow-all default and the real impl deferred to the Licensing module; the `X-Actor-Id` trusted header is removed; no migration / no `Domain` / no out-of-scope change; the OpenAPI contract regenerates green; Release build clean; full suite (unit + arch + integration) green. Then state → `verify` and route to LL QA Reviewer. Production exposure of the WP05 endpoints additionally requires WP09 (idempotency); real Entra token validation + identity-links is P2-WP11; real license enforcement is the Licensing module WP (recorded cross-WP invariants).

## Risks / notes

- **AuthN is configured but test-substituted here.** WP06 wires JWT bearer *options* and unit-tests their shape, but authorization is exercised through a test authentication scheme (no live Entra). This is deliberate: it makes access-control independently verifiable now while the real IdP integration (JWKS, issuer-pattern allowlist, identity-links, sign-out) is the focused P2-WP11. Do not treat WP06 as production AuthN.
- **Subject-GUID == user_id is a Phase-2 simplification.** Until the identity-links table (WP11) exists, the token subject GUID is used directly as `memberships.user_id`. Documented so QA and WP11 both know the seam.
- **License stage is a seam, not enforcement.** The allow-all default must not be mistaken for "no licensing"; the ordering test proves the seam is called in the right place so the Licensing module can drop in a real check without touching the ledger endpoints.
- **RLS is the second wall, not the only wall.** The filter is the application check; the space-bound policies remain the authoritative isolation. Both are tested independently; do not remove either. Alert-on-RLS-violation (§9) is an ops concern, not this WP.
- **Membership read under RLS.** Resolving the caller's role binds the route `spaceId` and filters by `userId`; the space-bound policy prevents cross-space reads. Binding must be txn-local (`is_local => true`) so the authz read cannot leak a space context into a pooled connection — same rule as the WP05 posting binding.
- **Endpoint contract change.** Removing `X-Actor-Id` and adding bearer auth changes the two endpoints' contract; the OpenAPI regen + updated WP05 HTTP tests capture it. Coordinate with any WP08 property-suite harness so it authenticates via the same test scheme.
- **No member management yet.** Roles are seeded directly in tests; assigning/inviting/changing roles is M2. WP06 only *reads* and *enforces* roles.

## Implementation notes

- **2026-07-11 — Implemented → verify.** Added configured JWT bearer authentication (`Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.4; package required after confirming it is not in the current Host graph) with audience/issuer allowlists, required signed tokens, `ICurrentUser`, and a test authentication scheme. Added typed `SpaceRole` parsing and server-side `ledger.post`/`ledger.reverse` permission mapping, plus reserved permission constants.
- Added `RequireSpacePermissionFilter` with the fixed authn → scope → license entitlement → membership → role permission order and stable ProblemDetails codes. The default `AllowAllLicenseEntitlement` is a per-request seam; no licensing schema was added.
- Added `ISpaceMembershipQuery`/Infrastructure implementation with transaction-local `leafledger_app`, `app.current_space_id`, and `app.current_actor` binding. Removed the trusted `X-Actor-Id` actor source; WP05 commands now receive the authenticated `oid`/`sub` subject through the validated principal.
- Protected both WP05 write endpoints, regenerated OpenAPI + TS artifacts with bearer security and 401/403 responses, and added HTTP authorization coverage for unauthenticated, scope, license, membership, role, reversal, RLS/actor binding, and spoofed-header cases.
- **Validation:** Release solution tests **227/227** (including architecture **3/3** and PostgreSQL integration **84/84**); focused authorization tests **36/36**; frontend tests **3/3**, lint, typecheck, page budget, and production build green. No migration, Domain, Licensing, identity-links, or non-generated frontend changes.

## QA verdict

- **QA PASS → done.** Independently reproduced local checks: Release build `0/0`, backend `227/227` (arch `3/3`, PostgreSQL integration `84/84`), frontend `3/3`, lint/typecheck/page-budget/build green. Addressed QA findings prior to sign-off: enforced required authorization callback on `MapLedgerEndpoints`, unified identity reading via `AuthorizationContext`, corrected literal placeholder issuer in `appsettings.json`, and corrected test count to `41/41`.
- **Golden Fidelity:** N/A (access control).
- **Scope & Boundaries:** Confirmed `ISpaceMembershipQuery` EF read is safely confined to Ledger `Infrastructure`; `Domain` is clean; RLS fail-closed confirmed with pooled connections via `OpenAppNoContextAsync`; OpenAPI generated correctly.
- **Security:** Token configuration (signed, audience/issuer allowlists) enforces strict shapes for WP11; `RequireSpacePermissionFilter` sequence confirmed via `AuthorizationFilterTests`; `ILicenseEntitlement` seam executes cleanly. Unauthenticated paths 401; missing scope/license/membership/permission 403; RLS replaces legacy header.
- **Carry-forwards recorded:** Real Entra E2E/identity-links (P2-WP11), Licensing module real enforcement, member-management UI (M2). Cleared for LL Git.
