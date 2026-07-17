# P4-WP01 — Design-system / visual-identity (CI/CD) foundation

- **Phase:** 4 (feature porting), **Stage A** (complete M1 — launch scope). **The first Phase-4 WP** — it establishes the LeafLedger **Corporate Identity / Corporate Design** (the visual design language: brand palette, typography, spacing/elevation, component styling, light + dark theming) so that every subsequent M1 feature page is built once against a settled, consistent design and does not require per-page ad-hoc styling or a later restyle (second-system risk #3 mitigation). Requested by the user 2026-07-17; scoped and drafted by LL Architect.
- **State:** done — **QA PASS 2026-07-17**. All seven front-loaded, **non-accounting** decisions are accepted (see Decisions). The five scoping choices the user made (2026-07-17) are folded in as constraints: **modern / clean, neutral-professional accounting direction**; **light + dark from day one**; **in-app `/design` gallery route + a short markdown guideline**; **soft dependency** (later feature pages may proceed in parallel and inherit the tokens). ADR-0008 records the token architecture decision.
- **Owner (implementation):** LL Frontend Dev. Single-agent, **frontend + docs only**. **No backend / OpenAPI / migration / accounting / golden-fixture change.**
- **Estimated size:** ~2 days (at the ceiling). If it exceeds ≤2 days, split at the documented **WP01a / WP01b** seam (see Split seam).
- **Depends on:**
  - **P3-WP04** (done) — the design-token **seam** and the shared primitives this WP skins: `app/src/shared/styles/tokens.css` (the single token file — currently a light-only warm palette: `--color-ink/paper/surface/line/accent`, `--space-*`, `--radius-*`, `--shadow-*`, `--font-sans`/`--font-heading`) and the primitives `DataTable`, `FormField`, `FormSection`, `MoneyInput`, `DateField`, `ModalShell`, `ToggleSwitch`, `listPrimitives`. **This WP extends `tokens.css` and re-skins these primitives via tokens — it does not rewrite them or change their public props.**
  - **P3-WP01** (done) — the app shell (`AppLayout`, providers, router) whose chrome this WP restyles and where the theme toggle + `/design` route mount; the providers tree where a `ThemeProvider` slots in (mirroring the i18n provider slot).
  - **P3-WP03** (done) — the i18n corpus + duplicate-key build gate + the render-edge formatters; gallery labels, the theme-toggle label, and the `/design` nav label are i18n keys in **both** locales; the theme-persistence pattern mirrors the i18n language-persistence decision (D-P3-I18N-PERSIST — `localStorage`, exempt from sign-out teardown).
- **Blocks (soft):** every subsequent **Stage-A UI WP** (P4-WP02 reports pages, P4-WP03 drill-down, P4-WP04 dashboard, P4-WP0x CoA/master-data/journal/imports/admin pages) cites this WP's tokens + `ui-design-guidelines.md` as the visual contract. Per the approved **soft-dependency** sequencing this is **not a hard block**: because all feature pages render only through the token-driven P3-WP04 primitives, a page built before or during this WP inherits the new design automatically once the tokens land.

## Context / scope note (LL Architect)

The [feature roadmap](../architecture/rebuild/06-feature-roadmap.md) M1 *Shell* row includes *"theming, date/number format prefs"* as M1 scope, and **P3-WP04 deliberately deferred theming to Phase 4** (decision D-P3-UI-THEME: *"defer theming → Phase 4"*). Phase 4 is therefore the correct home for the visual design language, and doing it **first** gives the remaining ~18 M1 pages a settled north star.

This is **not** a redesign of flows, information architecture, or interaction patterns, and **not** a new component library. It is: a coherent **token system** (color / type / spacing / elevation / motion) covering **light and dark**, a **theme runtime** (provider + toggle + persistence), **re-skinning the existing P3-WP04 primitives + shell chrome via those tokens** (visual only — no API/props change), an **in-app `/design` component gallery** and a short **`ui-design-guidelines.md`** as the living reference later WPs cite, and a **mechanical enforcement gate** (a `tools/` red-gate + lint) that keeps "the rest" consistent by forbidding hardcoded colors/spacing outside the token file.

**Guardrails against second-system scope growth (risk #3):** build on P3-WP04, do not rewrite it; visual-only changes to primitives (public props unchanged); no new runtime UI dependency (CSS custom properties + a tiny provider, no CSS-in-JS, no component library — D-P4-DS-TOKENS); the deliverable is bounded to tokens + theme + skinning + gallery + guideline + enforcement, with concrete testable acceptance criteria (below), not an open-ended "make it pretty" pass.

This WP introduces **no accounting behavior, no financial value, and no data access** — it is purely presentation/theming. Therefore **no golden fixtures and no LL Accounting Expert consult** (see those sections). Money still formats only at the render edge via the P3-WP03 formatters; this WP only styles how those already-formatted amounts look (e.g. tabular-nums, right-alignment) — it performs no float math and no amount computation.

## Spec sources

- `docs/architecture/rebuild/06-feature-roadmap.md` M1 *Shell* (theming, date/number format prefs, desktop-first min-width ~tablet landscape; all 5 locales) — the M1 shell scope this WP's design layer serves.
- `docs/architecture/rebuild/03-target-architecture.md` §7 (frontend structure: `shared/` owns UI primitives, tokens, formatting; app shell owns providers/layout; features → application → api — a design/token layer lives in `shared/` + the shell).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 4 (feature porting), §5 salvage (React UI primitives = salvage; the *look* is re-established here as a spec-derived design pass, not a port of OLD CSS), risk #3 (golden masters / tight scope define done — applied here as concrete testable ACs + a hard scope boundary).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 (reuse shared primitives — this WP skins them, does not replace them).
- `.github/instructions/frontend.instructions.md` — desktop-first; **design tokens via CSS custom properties**; reuse shared primitives before new UI; page budgets (450 lines / 30 imports / 20 state hooks); i18n every string in all locales; money integer minor units formatted only at the render edge (no float math on amounts).
- `docs/rebuild/plans/P3-WP04-shared-ui-primitives.md` — the token seam (`tokens.css`) + primitive inventory this WP extends and skins; D-P3-UI-TOKENS (canonical `tokens.css`) and **D-P3-UI-THEME (defer theming → Phase 4)** — the deferral this WP fulfils.
- `docs/rebuild/plans/P3-WP03-i18n-corpus-import.md` — D-P3-I18N-PERSIST (`localStorage` preference, exempt from sign-out teardown) — the pattern the theme preference mirrors.

## Goal

The whole app adopts a single, modern, clean, neutral-professional accounting design language, switchable between a **light** and a **dark** theme, driven entirely by CSS custom-property tokens; the existing primitives and shell chrome render through those tokens; a `/design` route documents the system live; a `ui-design-guidelines.md` records the rules; and a red-gate enforces token usage so the ~18 later M1 pages stay consistent by construction. Concretely:

1. **Tokens** — extend `app/src/shared/styles/tokens.css` into a full system: color roles (brand/primary + neutral scale + semantic success/warning/danger/info + surface/background/border/text roles), a typographic scale (families, sizes, weights, line-heights), spacing scale, radius scale, elevation/shadow scale, focus-ring, z-index scale, and minimal motion tokens — each defined for **light** (`:root`) and **dark** (`:root[data-theme='dark']`).
2. **Theme runtime** — a `ThemeProvider` + `useTheme()` hook that sets `data-theme` on `<html>`, defaults to `prefers-color-scheme`, allows an explicit light/dark override persisted in `localStorage` (key e.g. `ll.theme`), and is **decoupled from sign-out teardown** (mirrors i18n language persistence); a theme toggle in the shell chrome.
3. **Skinning** — apply the tokens to the P3-WP04 primitives and the `AppLayout` shell (nav/header/sidebar/content) so the app visibly adopts the design language in both themes; **no primitive prop/API change**.
4. **Reference** — a lazy `/design` gallery route (token swatches, type scale, spacing/elevation, every primitive in its states, a light/dark preview) + a short `docs/architecture/rebuild/ui-design-guidelines.md` (principles + token catalog + usage rules) that later WPs cite.
5. **Enforcement** — a `tools/check-design-tokens.cjs` red-gate (+ lint rule) that fails when a hardcoded color (hex/rgb/hsl) or raw spacing value is introduced in `app/src/**` outside `tokens.css` (and the gallery), wired into the frontend gates.

This WP adds **no** new feature page, **no** data access, **no** backend/contract/OpenAPI/migration change, and **no** new runtime UI dependency.

## Scope

1. **Tokens — `app/src/shared/styles/tokens.css`:** replace the current light-only warm palette with a modern/clean neutral-professional system (D-P4-DS-DIRECTION), defined for both themes:
   - **Color roles** (semantic, not raw hues): `--color-bg`, `--color-surface`, `--color-surface-raised`, `--color-border`, `--color-text`, `--color-text-muted`, `--color-text-inverse`; brand `--color-primary` (+ `-hover`/`-subtle`/`-contrast`); a neutral scale `--color-neutral-{50…900}`; semantic `--color-{success,warning,danger,info}` (+ `-subtle`). Light in `:root`; dark overrides in `:root[data-theme='dark']`.
   - **Typography:** `--font-sans` (a modern system UI stack), `--font-mono` (tabular numerals for amounts), a type scale `--text-{xs,sm,base,lg,xl,2xl}` with matching line-heights and `--font-weight-{regular,medium,semibold,bold}`.
   - **Spacing / radius / elevation / focus / z-index / motion:** keep/extend `--space-*`, `--radius-*`, `--shadow-*`; add `--focus-ring`, `--z-{base,dropdown,modal,toast}`, `--motion-fast/base` + `--ease-standard`.
   - Preserve every token name the P3-WP04 primitives already consume (rename only with a matching primitive update in the same WP) so nothing breaks.
2. **Theme runtime — `app/src/app/theme/ThemeProvider.tsx` + `useTheme.ts`:** context provider mounted in the providers tree above the router; sets `data-theme` on `document.documentElement`; initial value = persisted `localStorage['ll.theme']` else `prefers-color-scheme`; `setTheme('light'|'dark'|'system')` persists; **not** cleared by the WP02 sign-out `queryClient.clear()`/teardown (documented exemption, mirroring i18n). A theme toggle control in `AppLayout` chrome (i18n label).
3. **Skinning — the P3-WP04 primitives + `app/src/app/AppLayout.tsx` (+ `index.css`):** update the CSS of `DataTable`, `FormField`, `FormSection`, `MoneyInput`, `DateField`, `ModalShell.module.css`, `ToggleSwitch.module.css`, `listPrimitives`, and the shell chrome to consume the new tokens and read correctly in light **and** dark. Amounts use `--font-mono` + tabular-nums + right-alignment. **Public component props/APIs unchanged** (visual-only; page budgets and existing tests stay green).
4. **Gallery — `app/src/features/design/DesignSystemPage.tsx` + lazy `/design` route in `router.tsx` + nav affordance:** renders color swatches (both themes), the type scale, spacing/radius/elevation samples, and every primitive in its key states; serves as living documentation. ≤ page budget (or split into sub-components).
5. **Guideline doc — `docs/architecture/rebuild/ui-design-guidelines.md`:** short — design principles (modern/clean/neutral/dense/desktop-first), the token catalog, usage rules ("use tokens, never hardcode colors/spacing"; amounts right-aligned tabular-nums; spacing rhythm; focus-visible; contrast targets; do/don't), and a pointer to `/design`. Linked from the rebuild README index and cited by later Stage-A WP plans.
6. **Enforcement — `tools/check-design-tokens.cjs` + `eslint.config.js`:** a Node check (mirroring `tools/check-page-budget.cjs` / `check-i18n-duplicate-keys.cjs`) that scans `app/src/**` for hardcoded color literals (and, best-effort, raw px in inline styles) outside `tokens.css` and the gallery, exiting non-zero on a violation; add an `npm` script and wire it into the build/CI gate set; an accompanying ESLint rule for inline-style color literals where practical. Red-gate proven (a deliberate hardcoded color fails; removing it passes).
7. **i18n — `en.json`, `de.json`:** theme-toggle label(s), `/design` nav label, and gallery section headers as keys in both locales; duplicate-key gate green.
8. **Tests:** per acceptance criteria.

**No** OpenAPI/TS regeneration (frontend-only); if any regeneration surfaces a diff, **stop** — it signals an unexpected backend change outside this WP.

## Non-goals (explicitly deferred)

- **No new feature page or data access** — this is presentation/theming only; the reports/CoA/master-data/etc. pages are their own WPs (starting P4-WP02).
- **No flow / IA / interaction redesign** — same routes, same primitives' behavior, same page structures; only their visuals + theming.
- **No new component library or CSS-in-JS runtime dependency** — CSS custom properties + a tiny provider (D-P4-DS-TOKENS).
- **No user-facing date/number *format* preferences** — that M1 *Shell* item (the render-edge formatters made user-configurable) is a **later** WP (P4-WP19 shell polish); this WP does the *theme* half of "theming, format prefs".
- **No MSIX packaging / installable-shell work** — also P4-WP19.
- **No logo/brand-asset production** — if the user supplies assets they are wired in; otherwise a neutral wordmark placeholder (no design-agency deliverables).
- **No responsive/phone layout** — desktop-first stands (phones = M3 companion).
- **No backend / contract / migration / accounting / golden-fixture change.**

## Decisions (front-loaded, non-accounting — awaiting user sign-off)

Seven decisions. **None is an accounting decision.** Five encode the user's 2026-07-17 scoping answers; two were LL Architect recommendations and are now accepted by the user.

- **D-P4-DS-DIRECTION — visual direction (user-set).** **Modern, clean, neutral-professional accounting UI** — desktop-first, dense, restrained brand accent, high legibility for numeric tables. Replaces the current warm/serif palette. (User answer, folded in.)
- **D-P4-DS-THEMING — theme scope (user-set).** **Light + dark from day one**, via `:root` / `:root[data-theme='dark']` token blocks + a `ThemeProvider` toggle; default follows `prefers-color-scheme`. (User answer.)
- **D-P4-DS-GALLERY — the reference artifact (user-set).** **An in-app `/design` component gallery route + a short `ui-design-guidelines.md`.** No Storybook (avoids new tooling/deps). (User answer.)
- **D-P4-DS-SEQUENCING — dependency type (user-set).** **Soft dependency** — the foundation lands first, but later feature-page WPs may proceed in parallel and inherit the tokens automatically. Not a hard block. (User answer.)
- **D-P4-DS-TOKENS — implementation substrate. ACCEPTED: extend the single `app/src/shared/styles/tokens.css` as the source of truth; CSS custom properties only; no CSS-in-JS and no component-library dependency.** Preserves the P3-WP04 D-P3-UI-TOKENS decision and adds zero runtime deps. *Alternative:* a CSS-in-JS/theming library → new dependency + a larger bundle + a migration of existing primitives. Rejected. Recorded in [ADR-0008](../../architecture/adr/ADR-0008-frontend-design-token-architecture.md).
- **D-P4-DS-PERSIST — theme preference storage. ACCEPTED: persist the explicit theme choice in `localStorage` (`ll.theme`), default to `prefers-color-scheme`, and exempt it from the WP02 sign-out teardown** (a UI preference, not user data — mirrors the i18n language decision D-P3-I18N-PERSIST). *Alternative:* reset theme on sign-out → surprising (a shared machine loses the preference). Rejected. Recorded in [ADR-0008](../../architecture/adr/ADR-0008-frontend-design-token-architecture.md).
- **D-P4-DS-ENFORCEMENT — keeping "the rest" consistent. ACCEPTED: a `tools/check-design-tokens.cjs` red-gate (+ an ESLint inline-style color rule) forbidding hardcoded colors/spacing in `app/src/**` outside `tokens.css` and the gallery, wired into the build/CI gates** — the mechanical guarantee that later WPs use tokens. *Alternative:* rely on code review only → drifts across ~18 pages. Rejected. Recorded in [ADR-0008](../../architecture/adr/ADR-0008-frontend-design-token-architecture.md).

## Accounting decisions

**None required — no LL Accounting Expert consult.** This WP introduces no accounting behavior, no financial value, and no data access. It styles presentation only. The standing money boundary is respected, not decided: amounts remain integer minor units + ISO currency, formatted only at the render edge by the P3-WP03 formatters; this WP only affects how those already-formatted strings *look* (font, alignment) and performs no float math.

## Golden fixtures

**None required.** No accounting function is ported and no financial value is computed. Correctness is pinned by **frontend component / provider / route tests + a `tools/` red-gate + contrast assertions** (see acceptance criteria), mirroring the P3-WP03/WP04 precedent (a shared-UI / shell surface is pinned by unit + gate tests, not golden fixtures). Recorded so QA does not expect a golden artifact.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout `C:\Programming\LeafLedger\Accounting`).

- **Reference only (design inspiration, non-port):** the OLD app's `ThemeContext`/`useTheme` and CSS variables were explicitly flagged **non-port** in P3-WP04; the new design language is a **spec-derived, fresh** system (D-P4-DS-DIRECTION), not a port of OLD CSS. The OLD look informs only continuity of *product feel*, not specific values.
- **Salvage (seam, not styling):** the P3-WP04 `tokens.css` seam + primitive inventory are extended and skinned here.

## Acceptance criteria (concrete tests — all must pass)

1. **Token contract** — a test asserts `tokens.css` defines the required token families (color roles, neutral scale, semantic colors, type scale, spacing, radius, elevation, focus, z-index) and that a representative set resolves to **distinct** values under `:root` (light) and `:root[data-theme='dark']` (dark).
2. **Theme runtime** — `ThemeProvider` sets `data-theme` on `document.documentElement`; `useTheme().setTheme('dark')` updates it and writes `localStorage['ll.theme']`; with no stored value the initial theme follows a mocked `prefers-color-scheme`; a `queryClient.clear()` / sign-out teardown does **not** clear the stored theme (persistence-exemption test).
3. **Theme toggle in shell** — a shell test asserts a toggle control exists, is labelled via i18n, and switches `data-theme` between light and dark.
4. **Token usage on primitives** — a test asserts the skinned primitives/shell reference token custom properties (e.g. rendered styles use `var(--color-*)`, not literals) for a representative primitive, and that the amount cell uses the mono/tabular treatment.
5. **Gallery route** — `router.test.tsx`: `/design` resolves lazily under `AppLayout` and renders token swatches + at least one instance of each primitive; a render test mounts `DesignSystemPage` without error in both themes.
6. **Enforcement red-gate** — `tools/check-design-tokens.cjs` exits non-zero when a hardcoded color literal is present in a component under `app/src/**` (proven by a temporary fixture or an inline test of the scan function) and exits zero on the clean tree; the npm script is wired into the gate set.
7. **Contrast (accessibility)** — a unit test computes WCAG contrast ratios from the token values and asserts text-on-background and text-on-surface meet **AA (≥ 4.5:1 normal text)** and semantic colors meet AA on their subtle backgrounds, in **both** light and dark.
8. **No functional regression** — the full existing frontend Vitest suite passes (existing page/shell/router tests green with only visual/token changes); page budgets intact (`DesignSystemPage` ≤ 450 lines / 30 imports / 20 state hooks, or decomposed).
9. **i18n presence** — the theme-toggle, `/design` nav, and gallery-section keys exist in **both** `en.json` and `de.json`; the duplicate-key build gate is green.
10. **Guideline doc** — `docs/architecture/rebuild/ui-design-guidelines.md` exists, documents the token catalog + usage rules + contrast targets, and is linked from the rebuild README index.
11. **Gates + no contract drift** — lint, typecheck, production build, strict page budget, duplicate-key check, `npm audit --omit=dev` (0 vulnerabilities), and the new design-token check pass; **`schema.d.ts` and `backend/openapi/leafledger-v1.json` are byte-unchanged** (no backend touched); no new runtime dependency added.

## Split seam (if > 2 days)

- **WP01a — Token + theme foundation + enforcement:** the full `tokens.css` light/dark system, `ThemeProvider`/`useTheme` + toggle + persistence, the `tools/check-design-tokens.cjs` red-gate + lint, and the shell-chrome skin. ACs 1–3, 6, 7, 11.
- **WP01b — Primitive skinning + gallery + guideline:** re-skin all P3-WP04 primitives via tokens, the `/design` gallery route, and `ui-design-guidelines.md`. ACs 4, 5, 8, 9, 10.

## Open questions

- None blocking. Carry-forwards: user-facing **date/number format preferences** and **MSIX packaging** → P4-WP19 (shell polish); optional **brand assets/logo** (wired in if the user provides them, else neutral placeholder). ADR-0008 is accepted and recorded; no further planning decision is open.

## Definition of done

All 11 acceptance criteria pass; state → `verify`; LL QA Reviewer acceptance review; then LL Git. Frontend + docs only; no backend / OpenAPI / migration / accounting / golden-fixture change; no new runtime dependency; layering (features → application → api) and page budgets intact; existing tests green.

## Implementation note — 2026-07-17

Implemented the light/dark semantic token system, persisted theme runtime and shell toggle, token-skinned primitives, lazy `/design` gallery, UI guidelines, and design-token red-gate. Added theme/provider coverage and preserved generated API/backend artifacts. Implementer validation: Vitest **99/99**, lint, typecheck, strict page budget, duplicate-key check, design-token gate, production build, red-gate probe, and `npm audit --omit=dev` (**0 vulnerabilities**) all pass. The repository currently contains only the existing EN/DE launch locales, so no absent FR/ES/IT files were created.

## QA verdict — 2026-07-17

**FAIL — state returned to `in-progress`.**

1. **AC5 blocking:** `app/src/app/router.tsx:8` has no `design` child route, while `app/src/app/AppLayout.tsx:15` links to `/design`; the required lazy gallery therefore resolves to the not-found route. No router or gallery render test covers this path.
2. **AC4/AC5 blocking:** `app/src/features/design/DesignSystemPage.tsx:21-24` does not render `ModalShell`, radius/elevation samples, or a light/dark preview. It also contains visible hardcoded English labels (`Form section`, `Name`, `Example`, `Date`, `Amount`, `Active`, `Ledger clarity`, and the descriptive sentence), violating the i18n requirement for all user-facing strings.
3. **AC6 blocking:** `tools/check-design-tokens.cjs:7-22` scans only color literals. It does not implement the required best-effort raw-spacing/inline-style check, and no accompanying ESLint inline-style color rule was added to `app/eslint.config.js`.
4. **AC1/AC7 evidence gap:** no test asserts the token families, light/dark representative values, or WCAG contrast ratios. The token declarations exist, but the required executable contract and AA assertions are absent.
5. **AC2/AC3/AC4 evidence gap:** `ThemeProvider.test.tsx` covers explicit/system initialization only; it does not cover query-cache/sign-out persistence exemption, shell toggle behavior, or primitive token/amount styling. The existing shell/router tests do not assert the new toggle or `/design` gallery.
6. **Scope hygiene:** the worktree also contains P3-WP09 plan/status edits, ADR-0008, and an untracked P4-WP02 plan outside the P4-WP01 implementation file list. These must be separated or explicitly assigned before WP01 can pass scope review.

**Executed gates:** frontend Vitest **99/99**, lint, typecheck, strict page budget, production build, duplicate-key check, `npm audit --omit=dev` (**0 vulnerabilities**), design-token check, `git diff --check`, generated-contract byte-stability, and backend Release/Testcontainers suite **351/351** all pass. These green gates do not close the acceptance findings above.

## QA remediation — 2026-07-17

All six findings are addressed:

- Added and tested the lazy `/design` route under `AppLayout`.
- Completed the gallery with translated labels, both theme previews, radius/elevation samples, every listed primitive, and a controlled `ModalShell` example; added light/dark render coverage.
- Extended the token red-gate to reject raw inline spacing/layout pixels and added an ESLint rule for inline color literals; both clean-tree and deliberate-failure probes pass.
- Added token-family, light/dark distinction, and WCAG AA contrast tests.
- Added theme persistence-after-query-clear, shell-toggle, and right-aligned mono/tabular amount tests.
- Scope review: P4-WP01 changes are limited to its frontend/docs/tool surface. Existing P3-WP09, P7-WP01, P4-WP02, and ADR-0008 worktree entries remain assigned to their own plans and were not reverted.

**Remediation gates:** Vitest **106/106**, lint, typecheck, strict page budget, duplicate-key check, design-token check plus color/spacing red-gate probes, production build, `npm audit --omit=dev` (**0 vulnerabilities**), `git diff --check`, and generated-contract byte-stability all pass. Production build retains only the existing SignalR pure-annotation and large-chunk warnings.

## QA acceptance review — 2026-07-17

**PASS — state advanced to `done`. No blocking findings.**

- AC1–AC7: token-family/light-dark contract, theme runtime and persistence exemption, shell toggle, primitive token/amount styling, lazy gallery route and both-theme gallery rendering, color/spacing enforcement probes, and WCAG AA contrast assertions independently passed.
- AC8–AC11: frontend regression and page budget passed; both locale key validation and duplicate-key checks passed; guideline documentation/linkage is present; all frontend gates, audit, and generated-contract stability passed.
- Scope/security/integrity: reviewed WP01 changes are frontend/docs/tooling only; no API/OpenAPI/backend/accounting/golden-fixture changes; no new runtime dependency; no direct data access or financial computation; no generic catch/self-healing layer introduced.
- Independent results: frontend Vitest **106/106**; backend Release suite **351/351** including architecture **3/3** and Testcontainers integration **182/182**; lint, typecheck, strict page budget, duplicate-key check, design-token clean-tree check, color and spacing failure probes, production build, and `npm audit --omit=dev` (**0 vulnerabilities**) all pass. `git diff --check` passed and `app/src/api/schema.d.ts` plus `backend/openapi/leafledger-v1.json` are unchanged.
- Scope note: unrelated P3-WP09, P7-WP01, P4-WP02, and ADR-0008 worktree entries remain assigned to their own plans; they were not counted as WP01 defects or reverted.

Residual non-blocking build output: existing SignalR pure-annotation warnings from the dependency and the existing large-main-chunk warning.
