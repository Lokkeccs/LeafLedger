# P2-WP11 — AuthN hardening: Entra `common` token validation (JWKS signature + audience allowlist + issuer-pattern allowlist)

- **Phase:** 2 (ledger core — deferred follow-up; Phase-2 exit gate already met at WP08).
- **State:** verify — QA re-review PASS 2026-07-12. Implementation and endpoint-level local-JWKS proof complete. No accounting decision required (token validation is access control, not accounting behavior → no golden fixtures, no LL Accounting Expert consult).
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP06 (the authenticated-principal seam: `AddJwtBearer` wiring, `AuthenticationConfiguration`, `ICurrentUser`, the `TestAuthHandler` integration path, and the `RequireSpacePermission` filter that consumes the validated subject/scopes). No schema dependency.
- **Blocks:** production exposure of the WP05/WP06 write path behind a *real* IdP (WP06 wired JWT bearer *options* but exercised authorization through a test scheme); **P2-WP13** (identity-links) which consumes a trusted `sub`/`oid` + `tid` produced by hardened validation.
- **Estimated size:** ≤ 2 days (issuer-pattern validator + audience allowlist fail-closed guard + JWKS-signature integration proof with a locally-minted signing key + unit tests on the `TokenValidationParameters` shape + config keys; **no migration, no endpoint/contract change**).

## Re-scope note (LL Architect, 2026-07-12)

The Phase-2 tracker row for WP11 read *"Entra `common` JWT validation (JWKS signature + audience allowlist + issuer-pattern per §8.1) + identity-links table (subject ↔ `user_id`) + sign-out state."* Delivered literally that is **two cohesive but distinct units**: (1) a token-validation hardening pass over the Host auth pipeline (no schema), and (2) a new **identity-links** data model (new migration, a cross-cutting resolver that replaces the WP06 `subject GUID == user_id` simplification, and re-wiring of the authorization + RLS actor path). Combined they exceed the ≤ 2-day / independently-verifiable rule — the same reason WP05 was split into WP05/WP09/WP10 and WP06 carved WP11. WP11 is therefore scoped to the **token-validation core** (the first clause of the title, §8.1 AuthN), and the identity-links half is carved into a new **P2-WP13** (status.md updated in the same change):

- **P2-WP11 (this plan)** — real Entra `common` access-token validation: **JWKS signature** validation (via OIDC metadata discovery on the configured authority), an **audience allowlist** enforced fail-closed, and an **issuer-pattern allowlist** that accepts any legitimate Entra tenant issuer (`https://login.microsoftonline.com/{tenantId}/v2.0`, plus the v1 `https://sts.windows.net/{tenantId}/` shape) **and binds the issuer's tenant to the token's `tid` claim** — closing weakness **S2.6** (`ValidateIssuer = false` + hardcoded audience + no issuer allowlist). Verified with a **locally-minted signing key** (no live IdP dependency in CI); the parameter shape and the accept/reject matrix are unit- and integration-proven.
- **P2-WP13 (new, proposed)** — **identity-links** table (Entra `sub`/`oid` + `tid` ↔ internal `user_id`) + a deterministic resolver that **replaces the WP06 Phase-2 simplification** (`subject GUID == memberships.user_id`) and **eliminates the OLD "candidate-guessing"/stale-space auto-join class** (weakness §2.5, §3-target verdict line 51). New EF migration (raw SQL, idempotency-migration precedent). Depends on WP11 (a trusted `sub`/`oid`+`tid`) and WP06.
- **Sign-out / client state clearing** — the §8.1 clause *"MSAL cache in `sessionStorage`; sign-out clears all app state"* is a **frontend** concern (MSAL config + state teardown) and lands in **Phase 3** (frontend re-platform, roadmap M0 "Entra auth (MSAL, `sessionStorage`)"). The backend is stateless bearer-token auth: there is no server session to clear. The auto-join class is **structurally** eliminated on the server by WP13's deterministic linking (no fuzzy candidate path exists in the new code), not by a server sign-out endpoint. Recorded as a non-goal here with the frontend owner named.

WP11 **hardens** the token gate in front of the WP05/WP06 endpoints; it does not add posting behavior, authorization rules, identity storage, or any schema.

## Goal

Make the Host accept **only** genuine Entra `common` access tokens: correctly signed (JWKS from the configured authority's OIDC metadata), addressed to this API (audience allowlist), and issued by a legitimate Entra tenant whose issuer matches the token's `tid` (issuer-pattern allowlist). Replace WP06's placeholder static `ValidIssuers` list — which cannot work for a multi-tenant `common` app because the issuer varies per tenant — with a pattern validator, and make audience validation **fail-closed** when misconfigured. The accept/reject decision is proven end-to-end against the real `JwtBearer` pipeline using a **test signing key** the suite controls, so every rejection path (wrong key, `alg=none`/unsigned, expired, wrong audience, malformed/foreign issuer, issuer-tenant ≠ `tid`) is a concrete test — with **no dependency on a live Entra tenant** in CI.

This closes the last "Replace" security finding on the auth path (S2.6) and turns WP06's *configured-but-test-substituted* AuthN into *validated* AuthN, while leaving the identity→`user_id` indirection (WP13) and the frontend MSAL/sign-out glue (Phase 3) as their own units.

## Scope

1. **Issuer-pattern allowlist (Host `Authorization`):**
   - A dedicated `EntraIssuerValidator` (an `IssuerValidator` delegate for `TokenValidationParameters.IssuerValidator`) that accepts an issuer **iff**: it matches the Entra issuer shape `https://login.microsoftonline.com/{tenantId}/v2.0` (and the v1 `https://sts.windows.net/{tenantId}/` shape) with `{tenantId}` a well-formed GUID; the `{tenantId}` **equals the token's `tid` claim** (rejects a token whose issuer tenant and `tid` disagree); and, when a non-empty `Authentication:TenantAllowlist` is configured, `{tenantId}` is in that allowlist (empty allowlist = accept any valid Entra tenant, the `common` default). An issuer that is absent, non-HTTPS, wrong host, or non-GUID tenant is rejected. Returns the validated issuer string on success, throws `SecurityTokenInvalidIssuerException` on failure.
   - Wire it via `TokenValidationParameters.IssuerValidator`; drop the placeholder static `ValidIssuers` list (superseded). `ValidateIssuer` stays `true` (never the OLD `false`).
2. **Audience allowlist, fail-closed (Host `Authorization`):**
   - `ValidateAudience = true` with `ValidAudiences` read from `Authentication:Audiences`. Add a **startup guard**: if the audience allowlist is empty/whitespace-only in a non-Development environment, throw at composition time (fail-closed) rather than silently accepting any audience. (Development may run with the seeded placeholder for local smoke.)
3. **JWKS signature validation (Host, via authority metadata):**
   - Keep `options.Authority = https://login.microsoftonline.com/common/v2.0`; `JwtBearer` discovers the JWKS and refreshes keys automatically (`ConfigurationManager` defaults). Assert `ValidateIssuerSigningKey = true`, `RequireSignedTokens = true`, and that no code path sets `RequireSignedTokens = false` or `IssuerValidator`/audience to a bypass. Automatic key-rotation refresh is a `JwtBearer`/`ConfigurationManager` default — documented, not re-implemented.
   - **Test seam (no live IdP):** integration tests configure a second, isolated `JwtBearer` scheme (or override `TokenValidationParameters.IssuerSigningKeys` / a stub `ConfigurationManager`) with a locally generated RSA key so the suite can mint tokens and exercise the **real** validation code against a controllable JWKS. This proves signature/issuer/audience/lifetime enforcement without an Entra round-trip and without weakening production config.
4. **`ICurrentUser` claim hardening (Host):**
   - Confirm `SubjectId` prefers `oid` then `sub` (WP06 behavior) and expose the tenant id (`tid`) on `ICurrentUser` so WP13 can key identity-links by `(subject, tid)`. This is an **additive** read of an already-validated claim — no new authorization behavior in WP11 (the subject still flows to the WP06 filter unchanged; WP13 introduces the `user_id` indirection).
5. **Configuration + docs (Host):**
   - `appsettings*.json`: keep `Authority`, `Audiences`; add optional `Authentication:TenantAllowlist` (array; empty ⇒ any valid Entra tenant); remove the misleading placeholder `ValidIssuers: ["…/{tenant-id}/v2.0"]` (replaced by the pattern validator). No secrets committed (authority/audience/tenant ids are public identifiers, not secrets).
6. **Tests:** unit tests for the issuer validator (accept/reject matrix) and the `TokenValidationParameters`/options shape (issuer-pattern wired, audience allowlist enforced, signed-tokens required, no bypass); integration tests that mint locally-signed tokens and drive a real endpoint through the real pipeline to prove the full accept/reject matrix and that the existing WP06 authorization outcomes are unchanged for a validly-signed principal.

### Accept/reject matrix (asserted by tests)

| Token | Expected |
|---|---|
| Correctly signed (test key), audience ∈ allowlist, issuer matches pattern & `tid` | authenticated → WP06 authz proceeds |
| Signed by a **different** key | 401 (signature) |
| **Unsigned** / `alg=none` | 401 |
| **Expired** (`ValidateLifetime`) | 401 |
| Audience **not** in allowlist | 401 |
| Issuer host/shape **malformed** (non-Entra, non-HTTPS, non-GUID tenant) | 401 |
| Issuer tenant ≠ token `tid` | 401 |
| Tenant **not** in a configured `TenantAllowlist` | 401 |
| No bearer token at all | 401 `auth.unauthenticated` (WP06 ProblemDetails, unchanged) |

## Non-goals (explicitly deferred)

- **No identity-links table / resolver — P2-WP13.** WP11 keeps the WP06 documented simplification (`subject GUID == memberships.user_id`) for the membership read and RLS actor binding. WP11 only *exposes* `tid` on `ICurrentUser` for WP13; it does not add the `(subject,tid) → user_id` indirection, any `identity_links`/`users` table, provisioning-on-first-login, or a migration.
- **No frontend MSAL / `sessionStorage` / sign-out UI — Phase 3.** No MSAL config move to `sessionStorage`, no client sign-out state teardown, no login/consent flow. The backend has no server session to clear (stateless bearer).
- **No live-Entra E2E in CI.** Real tenant token acquisition is not automated; validation is proven with a locally-minted signing key against the real pipeline. A manual/staging smoke against a real tenant is an ops step at deploy, not a WP11 gate.
- **No Licensing / entitlement change.** The `ILicenseEntitlement` seam (allow-all) is untouched; real enforcement remains the Licensing module.
- **No authorization-rule change.** `SpaceRole`, the permission map, the filter ordering, membership read, and the two protected endpoints are unchanged. WP11 only strengthens the AuthN gate *in front of* them; 401/403 codes and 422/404 domain codes are byte-unchanged.
- **No new endpoint / no contract change expected.** The bearer security scheme + 401/403 responses were already added to the OpenAPI doc by WP06; WP11 adds no route and should produce **no** OpenAPI/TS drift (asserted — the `contract` gate must stay green with no regen needed). If a genuine drift surfaces it is a plan amendment, not silent regen.
- **No rate limiting, no token-revocation/denylist.** Stateless bearer; revocation is out of scope.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (not salvaged as code) — the OLD token gate is "Replace" (weakness S2.6)
- `backend/LeafLedger.Auth/AuthServiceExtensions.cs` — OLD `JwtBearer` config with **`ValidateIssuer = false`** ("issuer varies per user … security maintained by audience + signature") plus a hardcoded audience and **no issuer allowlist**. This is exactly weakness **§2.6** (broadened token acceptance). WP11 **replaces** it with issuer-pattern-allowlist + `tid` binding while keeping the audience + signature checks. No code is ported.
- `backend/LeafLedger.Auth/ClaimsPrincipalExtensions.cs` — `TryGetOid` (stable-subject precedence: `oid` → `sub`; synthetic-OID handling for the consumers tenant `9188040d-6c67-4c5b-b112-36a304b66dad`), `GetTid`, and the `GetIdentityCandidates`/`GetAccessCandidates` **candidate-guessing** used to fuzzily match a membership by many possible ids/emails. The candidate-guessing is the mechanism the target spec **replaces with identity-links** (verdict line 51) and is the root of the stale-space auto-join class (§2.5) — it is **WP13's** concern, not ported here. WP11 reuses only the *idea* of `oid`→`sub` subject precedence and the `tid` claim; the fuzzy multi-candidate matching is deliberately **not** reproduced.

### Rewrite (spec-derived; no OLD oracle)
- The `EntraIssuerValidator`, the audience fail-closed guard, and the local-signing-key integration proof are greenfield per §8.1. They are pinned by **unit + integration tests** (the accept/reject matrix), which is the correct instrument for token-validation behavior — not golden fixtures.

## Accounting decisions

**None.** Token validation is access control, not accounting behavior. No golden fixtures, no LL Accounting Expert consult. (Recorded explicitly so QA does not expect an accounting artifact.)

## Golden fixtures

**None required.** WP11 changes no accounting rule and produces no financial output. Token-validation behavior is pinned by unit tests (issuer validator + options shape) and integration tests (locally-signed accept/reject matrix through the real pipeline).

## Dependencies

- **Decision D-WP11-ISSUER (issuer validator source):** default = a small **hand-rolled** `EntraIssuerValidator` using only the BCL + already-referenced `Microsoft.IdentityModel.Tokens` (no new production package; keeps the dependency surface minimal per the repo's no-new-dependency rule). Documented alternative = `Microsoft.IdentityModel.Validators`' `AadIssuerValidator` (canonical Microsoft implementation) — adopt **only** if the hand-rolled validator proves insufficient, and record the package here before use. The hand-rolled path is preferred and expected to suffice.
- Test project (`LeafLedger.IntegrationTests`) already has `Microsoft.AspNetCore.Mvc.Testing` / `TestHost` (WP05/06). Minting locally-signed tokens uses `System.IdentityModel.Tokens.Jwt` / `Microsoft.IdentityModel.Tokens` (already in the test graph via `JwtBearer`); confirm before use and record if a version-pinned test package is needed.
- No new production NuGet expected. `Microsoft.AspNetCore.Authentication.JwtBearer` (added by WP06) already provides the pipeline.

## File list (implementation target)

**New — `backend/src/LeafLedger.Host/Authorization/`**
- `EntraIssuerValidator.cs` — the `IssuerValidator` delegate + a testable static method (`Validate(issuer, tid, tenantAllowlist)`), with the Entra v1/v2 issuer shapes and `tid` binding.

**Modified**
- `backend/src/LeafLedger.Host/Authorization/AuthenticationConfiguration.cs` — wire `IssuerValidator = EntraIssuerValidator...`; drop static `ValidIssuers`; read `TenantAllowlist`; add the audience fail-closed guard (throw when empty outside Development).
- `backend/src/LeafLedger.Host/Authorization/ICurrentUser.cs` (+ `HttpContextCurrentUser`) — expose `TenantId` (`tid`) read from the validated principal (additive; consumed by WP13, not by WP11 behavior).
- `backend/src/LeafLedger.Host/Program.cs` — only if the audience guard or issuer-validator wiring needs a composition-time call; otherwise unchanged (the bearer scheme is already registered).
- `backend/src/LeafLedger.Host/appsettings.json` / `appsettings.Development.json` — add optional `Authentication:TenantAllowlist`; remove the placeholder `ValidIssuers`. No secrets.
- `backend/openapi/leafledger-v1.json` and `app/src/api/schema.d.ts` — **expected unchanged** (no route/response change). Do not hand-edit; the `contract` gate must stay green with no regen.

**New — tests**
- `backend/tests/LeafLedger.Host.Tests/` (or the existing Host-level unit test project; create if absent — same choice WP06 flagged) — unit tests for `EntraIssuerValidator` (full accept/reject matrix: good v2, good v1, `tid` mismatch, non-GUID tenant, wrong host, non-HTTPS, empty, tenant-allowlist in/out) and for `AuthenticationConfiguration` (issuer-validator wired, `ValidateIssuer`/`ValidateAudience`/`RequireSignedTokens` true, audience guard throws on empty outside Development, no `ValidateIssuer=false` / `RequireSignedTokens=false` path).
- `backend/tests/LeafLedger.IntegrationTests/Authorization/EntraTokenValidationTests.cs` (`[Trait("Category","Integration")]`) — a `WebApplicationFactory` that registers a `JwtBearer` scheme bound to a locally generated RSA signing key + stub metadata, then drives a real protected endpoint (e.g. `POST …/journal-entries`, seeded like WP06) to assert the accept/reject matrix and that a validly-signed `Member`+ principal still gets the WP06 outcome (201/422/404 unchanged). Reuses the WP06 seed/membership helpers.

**Docs**
- `docs/rebuild/plans/P2-WP11-authn-token-validation.md` + `docs/rebuild/status.md` — notes/state.

No files under `app/src/**` except (expected-empty) regenerated `api/**`; **no new migration**; no `*.Domain` change; no Ledger `Infrastructure` change; no Licensing/identity-links schema.

## Boundary note

- All auth-validation code lives in the **Host** (`Program.cs`, DI, middleware pipeline per §5). The Host may reference ASP.NET Core + `Microsoft.IdentityModel.*`. The arch tests must stay green unchanged: `*.Domain` → SharedKernel only; `EfCoreIsConfinedToInfrastructureNamespaces` (WP11 touches no EF); no auth type leaks into `Domain`.

## Implementation sequence

1. Add `EntraIssuerValidator` + unit tests (accept/reject matrix, `tid` binding, tenant allowlist).
2. Wire it into `AuthenticationConfiguration`; drop static `ValidIssuers`; add the audience fail-closed guard; unit-test the options shape + guard.
3. Expose `tid` on `ICurrentUser` (additive read); unit-test.
4. Add the `EntraTokenValidationTests` integration suite with a local RSA signing key; prove the full matrix and WP06-outcome preservation.
5. Update `appsettings*` (`TenantAllowlist`; remove placeholder issuer). Confirm the OpenAPI/TS `contract` gate is green with **no** regen.
6. Run Release build + arch tests + full suite (incl. integration under Docker) + lint/typecheck/page-budget + contract gate; document results/deviations; move to `verify`.

## Implementation notes

- **2026-07-12 — LL Backend Dev:** Added the hand-rolled `EntraIssuerValidator` for Entra v1/v2 issuer shapes, HTTPS/GUID validation, issuer-tenant=`tid` binding, and optional `TenantAllowlist`; replaced the placeholder `ValidIssuers` configuration with the validator delegate; added the non-Development empty-audience startup guard; exposed `ICurrentUser.TenantId`; removed the source `ValidIssuers` placeholder from `appsettings.json`.
- **2026-07-12 — LL Backend Dev:** Added issuer/tid/tenant-allowlist unit coverage and a locally minted RSA validation proof using the production `TokenValidationParameters` shape. Focused authorization tests pass **28/28**; Release build passes; full backend non-property suite passes **285/285** (integration **122/122**); frontend lint/typecheck/tests/page-budget and the no-drift contract check pass.
- **2026-07-12 — LL Backend Dev:** Added `EntraTokenValidationTests` using `WebApplicationFactory`, the real production `JwtBearer` scheme, a locally generated RSA key, and Docker-backed PostgreSQL. Endpoint matrix passes **7/7**: valid Member authorization, wrong signature, unsigned token, expired token, wrong audience, malformed issuer, and issuer/`tid` mismatch. Final Release backend suite passes **292/292** (integration **129/129**); no production route, schema, OpenAPI, or generated-client changes were required. State → **verify**; next LL QA Reviewer.
- **2026-07-12 — LL Backend Dev:** Fixed QA findings: issuer parsing now rejects non-default ports, user-info, query, fragment, and non-exact Entra paths; endpoint tests now cover valid v1 issuers, missing/query/fragment issuers, and configured tenant-allowlist accept/reject behavior through the real JwtBearer pipeline. Focused authorization/JWT tests pass **41/41**; full Release backend suite passes **298/298** (integration **135/135**); architecture tests pass **3/3**. State remains **verify**; next LL QA Reviewer.

## Acceptance criteria (concrete tests)

1. **Build, boundaries & contract:** `dotnet build LeafLedger.sln -c Release` = 0/0; architecture tests stay green (`*.Domain` → SharedKernel only; EF confined to `Infrastructure`; no auth type in `Domain`); the CI `contract` gate is green with **no OpenAPI/TS regeneration** (WP11 adds no route/response) — a regen-produced diff is a failure.
2. **Signature enforced:** a token signed by the trusted test key authenticates and proceeds to WP06 authorization; a token signed by a **different** key is **401**; an **unsigned**/`alg=none` token is **401** (`RequireSignedTokens`/`ValidateIssuerSigningKey` proven, not merely configured).
3. **Audience allowlist:** a token whose `aud` is **not** in `Authentication:Audiences` is **401**; a token with an allowlisted `aud` passes. **Fail-closed guard:** composing the app with an empty audience allowlist outside `Development` **throws** at startup (a unit/host test asserts the throw).
4. **Issuer-pattern allowlist:** a token whose issuer matches `https://login.microsoftonline.com/{guid}/v2.0` (or v1 `https://sts.windows.net/{guid}/`) with `{guid} == tid` passes; issuers that are non-HTTPS, wrong host, non-GUID tenant, absent, or whose tenant ≠ `tid` are **401**. `ValidateIssuer` is `true` and there is **no** `ValidateIssuer = false` path anywhere (asserted).
5. **Tenant allowlist (optional):** with a non-empty `Authentication:TenantAllowlist`, a token from a tenant **not** in the list is **401**; a listed tenant passes; with an **empty** allowlist any valid Entra tenant passes (the `common` default).
6. **Lifetime:** an **expired** token is **401** (`ValidateLifetime`).
7. **Issuer-validator unit matrix:** `EntraIssuerValidator` returns the issuer for every valid case and throws `SecurityTokenInvalidIssuerException` for every invalid case in the matrix above — covered by a `[Theory]` (no live IdP).
8. **`tid` exposed:** `ICurrentUser.TenantId` returns the validated `tid` for an authenticated principal and `null`/none when absent — additive; the WP06 subject resolution (`oid`→`sub`) is unchanged.
9. **WP06 behavior preserved:** with a validly-signed `Member`+ principal, the existing WP06/WP05 outcomes are unchanged — 201 happy path, 401 unauthenticated, 403 non-member/permission, 422 domain, 404 missing target; the filter ordering and ProblemDetails codes are byte-unchanged. WP11 only adds signature/issuer/audience rejection **in front of** authentication success.
10. **No live-IdP dependency / no secrets:** the integration suite runs with a locally-minted key and **no** network call to Entra; no secret is committed (authority/audience/tenant ids are public identifiers); production `appsettings` contains no key material.
11. **Scope containment:** `git diff --name-only` is limited to the file list; **no new migration**, no `*.Domain`/Ledger `Infrastructure` change, no Licensing/identity-links schema, no frontend beyond (expected-empty) `api/**`; the existing Ledger integration suite (incl. `HasPendingModelChanges()`) stays green (schema untouched).
12. **Quality gate:** lint, typecheck, page budgets, arch/boundary tests, and the relevant unit + token-validation integration tests all pass in Release; integration tests run under Docker (`postgres:17`) via the `Category=Integration` filter and are green on the main-branch job.

## Definition of done

All 12 ACs pass; the Host accepts only correctly-signed Entra `common` tokens with an allowlisted audience and a pattern-valid issuer bound to `tid`, rejecting every other case with 401; the placeholder static issuer list and the S2.6 `ValidateIssuer=false` risk are gone; audience validation is fail-closed; `tid` is exposed for WP13; WP06 authorization outcomes and the OpenAPI contract are unchanged (no regen); no migration / no `Domain` / no out-of-scope change; Release build clean; full suite (unit + arch + integration) green. Then state → `verify` and route to LL QA Reviewer. Identity→`user_id` indirection (identity-links) is **P2-WP13**; the frontend MSAL/`sessionStorage`/sign-out glue is **Phase 3**; a real-tenant staging smoke is an ops deploy step, not a WP11 gate.

## QA verdict

**FAIL — 2026-07-12, LL QA Reviewer.** The executable gates reproduced successfully: Release build `0/0`; focused authorization/JWT tests `35/35`; architecture tests `3/3`; full Release backend suite `292/292` (`129` integration, `0` failures). The WP remains `in-progress` pending the findings below.

1. **Issuer shape is not strict enough.** `backend/src/LeafLedger.Host/Authorization/EntraIssuerValidator.cs` validates only `Uri.AbsolutePath` and then returns the original issuer. Because query and fragment components are ignored by `AbsolutePath`, values such as `https://login.microsoftonline.com/{tenantId}/v2.0?unexpected=1` or the equivalent fragment form are accepted even though the acceptance criteria require the exact Entra issuer shape and malformed issuers to be rejected. The parser must reject non-empty query/fragment (and add regression coverage) before this criterion can pass.
2. **The complete acceptance matrix is not proven through the configured pipeline.** `backend/tests/LeafLedger.IntegrationTests/Authorization/EntraTokenValidationTests.cs` exercises only a valid v2 issuer. The v1 issuer acceptance and the `Authentication:TenantAllowlist` in/out behavior are tested only by calling `EntraIssuerValidator.Validate` directly in `AuthorizationModelTests`; no WebApplicationFactory test proves that the configured allowlist is applied, and no endpoint test proves the valid v1 path. Add pipeline-level tests for those cases, plus absent/empty and query/fragment malformed issuers, before sign-off.

**Scope note:** the current working tree also contains unrelated WP08 documentation changes alongside WP11 files, so the declared WP11 file-list check is not clean until the changes are separated or explicitly accounted for.

**QA re-review PASS — 2026-07-12, LL QA Reviewer.** Both findings are closed. `EntraIssuerValidator` rejects query/fragment and other non-exact issuer variants while accepting the implicit HTTPS default port. The real JwtBearer endpoint suite covers valid v1 issuance, missing/query/fragment rejection, and tenant-allowlist acceptance/rejection. Focused tests pass **41/41**; full Release backend suite passes **298/298** (`135` integration); architecture tests pass **3/3**; `git diff --check` is clean. The unrelated WP08 documentation changes remain pre-existing working-tree scope and must be separated before commit.

**Independent re-review PASS — 2026-07-12, LL QA Reviewer.** Reproduced the focused WP11 suite (**41/41**), Release build (**0/0**), full backend Release suite (**298/298**, including **135/135** Docker-backed integration and **3/3** architecture tests), frontend lint/typecheck/tests (**3/3**)/page budget/build, and contract stability (no OpenAPI or generated-client diff). No new behavioral, financial-integrity, security, or patch-layering findings. The unrelated WP08 plan modification remains outside WP11's declared file list and must be separated before commit.

## Risks / notes

- **`common` means the issuer is per-tenant.** A static `ValidIssuers` list cannot validate a multi-tenant app; the pattern validator + `tid` binding is the correct mechanism (matches Microsoft's `AadIssuerValidator`). If the hand-rolled validator misses an edge (e.g. CIAM/`ciamlogin.com` or sovereign-cloud hosts), switch to `Microsoft.IdentityModel.Validators` (record the dependency) — but the target IdP is commercial Entra `common`, so the two documented shapes suffice.
- **Local-key test seam must not weaken production.** The RSA-key/stub-metadata seam lives only in the test `WebApplicationFactory`; production keeps `Authority`-driven JWKS discovery. A test must assert production config does **not** set `IssuerSigningKeys`/disable metadata.
- **Audience fail-closed is a tightening.** Throwing on an empty audience outside Development is deliberate (fail-closed) and safe pre-production; Development retains the seeded placeholder so local `docker compose` smoke still runs.
- **Contract must not drift.** WP11 adds no route; a stray OpenAPI/TS regen would be an accidental diff. The plan requires the `contract` gate green **without** regen — treat any diff as a finding.
- **Consumers/personal accounts.** Synthetic-OID personal-account handling (OLD `LooksSyntheticOid`) is a subject-resolution nuance for WP13 (which claim becomes `user_id`), not a token-validation concern; WP11 validates the token and exposes `tid`, leaving the subject→`user_id` mapping to WP13.
- **This is the AuthN half only.** Do not treat WP11 as "identity done": until WP13, `memberships.user_id` is still the raw subject GUID. WP11 makes the *token* trustworthy; WP13 makes the *identity mapping* deterministic.
```