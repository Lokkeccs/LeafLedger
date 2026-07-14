# P3-WP03 — i18n corpus import (EN + DE to start; FR/ES/IT deferred) + money/date/number formatting at the render edge

- **Phase:** 3 (frontend re-platform)
- **State:** verify — **all decisions approved by the user 2026-07-14** (D-P3-I18N-LIB / -PERSIST / -FORMAT / -CORPUS on their recommended routes; **plus D-P3-I18N-LOCALES: start with EN + DE only**, FR/ES/IT deferred to a follow-up).
- **Owner (implementation):** LL Frontend Dev
- **Depends on:**
  - **P3-WP01** (done) — the app-shell provider tree (`AppRoot`), the desktop-first `AppLayout`, and the documented **i18n provider slot** carry-forward. WP03 fills that slot and replaces WP01's temporary-English shell chrome.
  - **P3-WP02** (done) — the MSAL sign-in/out chrome in the layout top bar (`useAuth`) that currently uses WP01-sanctioned temporary English placeholders ("Sign in" / "Sign out" / "Sign-in not configured"). WP03 replaces those with translation keys and defines how the language preference interacts with the WP02 sign-out teardown (D-P3-I18N-PERSIST).
- **Blocks:** every later Phase-3 page — WP04 primitives, WP05 accounts page, WP06 journal-entry page, WP07 trial-balance report all render user-facing copy (translation keys, EN + DE) and display money/dates/numbers, which must be formatted at the render edge by the utilities this WP delivers.
- **Estimated size:** ≤ 2 days (copy the EN + DE locale JSONs verbatim + i18next init module + provider mount + port the duplicate-key build check + render-edge money/date/number formatters and hooks + replace shell chrome strings + tests). **No backend, no OpenAPI/TS regeneration, no new API surface.**

## Context / re-scope note (LL Architect, 2026-07-14)

Part 4 §Phase 3 bundles the whole frontend re-platform; Phase 3 was decomposed into 8 WPs (P3-WP01…08) on 2026-07-12. **WP03 is the internationalization + formatting slice only:** import the salvaged i18next corpus (see the locale-scope decision below), stand up the i18n runtime and provider, keep the duplicate-key build guard, replace the WP01/WP02 temporary shell strings with keys, and deliver the **render-edge** money/date/number formatters the pages consume. Authentication is WP02 (done), primitives are WP04, pages are WP05–WP07, live invalidation is WP08. WP03 adds **no** page and **no** new endpoint call.

**Locale-scope override (D-P3-I18N-LOCALES, user-approved 2026-07-14):** the spec mandate is "corpus ports wholesale" / "all 5 locales from day one" (roadmap M1 Shell), but the user has chosen to **start with EN + DE only** and defer FR/ES/IT to a later follow-up. WP03 therefore imports the OLD `en.json` and `de.json` **verbatim** (the two languages that ship first); the i18next runtime, provider, formatters, duplicate-key guard, and "key-in-every-active-locale" discipline are all built to be locale-count-agnostic so adding FR/ES/IT later is a pure content import (drop the three JSONs in + register them, no runtime change). This is a deliberate, recorded divergence from the roadmap's "all 5 from day one" line — flagged so a QA reviewer does not treat the two-locale set as an omission. The only enforced structural guard the OLD app had here — the duplicate-key check inside `npm run build` — is preserved (it "earned its place", §5).

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §2 salvage table line 45 (**"i18next + 5-locale corpus = Keep"** — "Years of translated domain vocabulary; **duplicate-key check stays in build**"), §7 (`shared/` holds "UI primitives, **i18n ×5, formatting**"; flow **features → application → api**), the diagram row "`shared/` (UI primitives, i18n ×5, formatting) … light TS pre-validation (UX only)".
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 ("New app shell: … **i18n corpus imported wholesale**; port `shared/` UI primitives …"), §5 salvage ("React UI primitives, feature-policy matrix, **i18n corpus**, MSAL flow shape" = salvage; "**all data access** Dexie → TanStack Query + generated client" = rewrite — the OLD **space-preference-driven** number/date format machinery is data-access-coupled and is **not** ported here).
- `docs/architecture/rebuild/05-quality-and-maintainability.md` §"PR template" ("**i18n keys added to all 5 locales?**"), §"i18n keys namespaced per feature; **duplicate-key check stays in build** (it earned its place)".
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 Shell ("**i18n (all 5 locales from day one — corpus ports wholesale)**, theming, date/number format prefs" — note: *format prefs* (admin-set space preferences) are an M1/M2 admin surface, **not** WP03; WP03 delivers only the render-edge formatting primitives keyed to the active locale).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 ("new i18n keys go into **ALL 5 locale files** in the same change").
- `docs/rebuild/plans/P3-WP01-app-shell-foundation.md` "Non-goals → No i18n — WP03" and "the one place WP01 is permitted temporary non-i18n copy … WP03 removes it"; the documented **i18n provider slot** in the provider tree.
- `docs/rebuild/plans/P3-WP02-msal-authentication.md` Scope §4 (sign-in/out chrome "uses the WP01 temporary-English placeholder convention (WP03 i18n replaces it)"; **sign-out clears all app state**) — WP03 must decide whether a device language preference counts as "app state" (D-P3-I18N-PERSIST).
- `.github/instructions/frontend.instructions.md` — **i18n:** "every new user-facing string is a translation key added to **all locale files** in the same change — never hard-code copy"; **Money:** "arrives as integer minor units + ISO currency; **format only at the render edge. Never do float math on amounts**"; page budget 450/30/20; "done means" lint/typecheck/test/page-budget green.

## Goal

Stand up the application's internationalization runtime by importing the OLD i18next corpus **verbatim** for the **two launch locales EN + DE** (D-P3-I18N-LOCALES), initializing i18next with the salvaged configuration shape, mounting the i18n provider into the WP01 provider slot (above the router, so the splash, shell chrome, error-boundary fallbacks, and every page can translate), and **preserving the duplicate-key build check** as a blocking gate. Replace the WP01/WP02 temporary English shell strings with translation keys present in **both** active locales. Deliver the **render-edge formatting layer** the later pages depend on: a `formatMoney` that renders integer minor units + ISO currency to a locale-correct string **with no float arithmetic**, plus locale-keyed date and number formatters, exposed as small hooks. The OLD `localStorage`-based language persistence is salvaged (renamed, and explicitly decoupled from the WP02 sign-out teardown); the OLD **space-preference-driven** number/date format machinery (`useNumberFormat`/`useDateFormat` reading Cosmos space preferences) is **not** ported — that admin surface is Phase 4. FR/ES/IT are a deferred content-only follow-up (no runtime change).

## Scope

1. **Locale corpus (verbatim salvage) — `app/src/i18n/locales/`:**
   - Copy the two launch locale files `en.json` and `de.json` **byte-for-byte** from the pinned OLD repo `src/i18n/locales/` (D-P3-I18N-LOCALES). No key added, removed, renamed, or re-translated. FR/ES/IT are **not** imported in this WP (deferred follow-up). Known OLD **content** defects in the imported files are ported **as-is** — correcting translation content is explicitly out of scope (D-P3-I18N-CORPUS); the duplicate-**key** check is the only structural guard. (The `it.json` Spanish-string defect noted elsewhere affects only the deferred Italian locale, not this WP's EN/DE set.)
2. **i18n runtime — `app/src/i18n/index.ts`:**
   - Initialize `i18next` with `initReactI18next`, `resources` = the two imported JSONs (`en`, `de`) under `translation`, `fallbackLng: 'en'`, `interpolation.escapeValue: false` (React escapes) — the salvaged shape. The resource map + supported-locale list are the **single place** the locale set is declared, so adding FR/ES/IT later is a one-line-per-locale registration. Language selection: read the saved preference (D-P3-I18N-PERSIST), else `'en'` (no browser auto-detection, matching OLD behavior — deterministic, no locale-guessing).
   - Export `setLanguage(lng)` (change + persist) and a small typed list of supported locales (code + label — `en`, `de` for now) for a future language switcher (the switcher UI itself is not built here — no settings page yet).
3. **Provider mount — `app/src/app/providers.tsx`:**
   - Wrap the WP01/WP02 tree in `I18nextProvider` (per D-P3-I18N-LIB) at the **outermost** shell level (above `MsalProvider`/`QueryClientProvider`/`RouterProvider`) so the initialization splash, the auth chrome, the `AppErrorBoundary`/`RouteErrorBoundary` fallbacks, and every route can call `t(...)`. i18next init is synchronous (resources are bundled) so no async i18n gate is required.
4. **Duplicate-key build guard — `tools/check-i18n-duplicate-keys.cjs` + `app/package.json`:**
   - Port the OLD `scripts/check-i18n-duplicate-keys.cjs` (adapted to resolve `app/src/i18n/locales`), add an `i18n:keys:check` script, and prepend it to the `app` `build` script (`i18n:keys:check && tsc -b && vite build`) — mirroring the OLD deploy gate. It must fail the build (non-zero exit) on any duplicate JSON key in any locale file. A red-gate proof (temporary duplicate → build fails → reverted) is part of acceptance.
5. **Render-edge formatting layer — `app/src/i18n/format/`:**
   - `formatMoney(minorUnits, currency, locale)` → a locale-correct currency string derived from **integer** minor units + ISO currency, with **no float division** (no `minor / 100`): the fraction-digit count comes from the currency, and the major/minor split is done with integer/string arithmetic before assembling via `Intl.NumberFormat` (currency symbol, grouping, and separators from the locale). Handles negative, zero, 0-decimal (e.g. JPY) and 3-decimal (e.g. BHD) currencies.
   - `formatDate(date, locale, opts?)` and `formatNumber(value, locale, opts?)` → thin `Intl.DateTimeFormat`/`Intl.NumberFormat` wrappers keyed to the **active i18n locale**.
   - `useMoneyFormat()`, `useDateFormat()`, `useNumberFormat()` hooks that bind the active locale from `useTranslation().i18n.language` and return the formatters — the render-edge seam WP05–WP07 consume. **Note:** these are locale-only; the OLD Cosmos-space-preference overrides ('system'/'en-US'/'de-DE'/'de-CH', accent color, fiscal settings) are **not** ported (Phase 4 admin surface).
6. **Replace temporary shell copy — `app/src/app/**`:**
   - Replace the WP01/WP02 hard-coded English strings in `AppLayout` (nav "Overview", "Workspace", "Online", auth chrome "Sign in"/"Sign out"/"Sign-in not configured"), `providers.tsx` splash ("Preparing secure workspace…"), `HomeRoute`, `NotFoundRoute`, and both error boundaries with `t('…')` keys. Every new key is added to **both active locale files (`en`, `de`)** in the same change (using the OLD keys where they already exist, e.g. `nav.*`, `auth.signInMicrosoft`; adding new shell keys where none exist). After this WP there are **no** hard-coded user-facing strings in the shell.
7. **Boundary + budget conformance:**
   - `app/src/i18n/**` is a cross-cutting **shared leaf module** (imports nothing from `features`/`app`/`application`; may be imported by all of them) — it is neither `api`, `app`, nor `features`, so no ESLint boundary rule blocks it (confirmed against `eslint.config.js`); no boundary-config change is required (if one proves necessary it is a documented deviation, not a loosening). Page budget green for every new file; the large locale JSONs are data, not "pages", and are excluded from the page-budget line/import counts (confirm the tool ignores `.json` or scope it to `.ts/.tsx`).

## Non-goals (explicitly deferred)

- **No FR/ES/IT locales yet — deferred follow-up (D-P3-I18N-LOCALES).** Only EN + DE ship in WP03. Because the runtime, formatters, and duplicate-key guard are locale-count-agnostic, adding the remaining three OLD locale JSONs later is a content-only change (import + register), not a WP-scale effort. Recorded so a QA reviewer does not treat the two-locale set as a gap against the roadmap's "all 5 from day one" line.
- **No space-preference-driven formatting — Phase 4.** The OLD admin-set `dateFormat`/`numberFormat` ('system'/'en-US'/'de-DE'/'de-CH'), accent color, fiscal-year/period/VAT preferences and their Cosmos sync (`useNumberFormat`/`useDateFormat`/`spacePreferencesApi`) are **not** ported. WP03 formats by the **active i18n locale** only.
- **No language-switcher UI / settings page — WP05+ / Phase 4.** WP03 provides `setLanguage()` + the supported-locale list; no settings/preferences page exists yet to host a picker (a minimal dev affordance is optional but not required).
- **No translation-content corrections.** Corpus ports verbatim including known OLD defects; fixing mistranslations is a separate content task, not WP03 (D-P3-I18N-CORPUS).
- **No new feature namespaces invented.** WP03 adds only the shell keys it needs to remove the temporary placeholders; feature keys already exist in the wholesale corpus and are used when those features land (WP05+).
- **No theming / PWA / help-content port.** Theming, the PWA shell, and the per-locale `helpDocs.*` content are separate concerns.
- **No backend change, no OpenAPI/TS regeneration.** `app/src/api/**` (generated) is byte-unchanged; the CI `contract` gate is unaffected.

## Decisions (front-loaded, non-accounting — all APPROVED by the user 2026-07-14)

Per the WP01/WP02/WP12/WP13 precedent, load-bearing structural choices with no accounting content are surfaced for a one-time user decision so implementation is unambiguous.

- **D-P3-I18N-LOCALES — launch locale set. APPROVED: start with EN + DE only; defer FR/ES/IT.** The user chose to ship the two primary languages first and add the remaining three OLD locales in a later content-only follow-up. This is a deliberate, recorded divergence from the roadmap "all 5 locales from day one" line. The runtime (`resources` map + supported-locale list), formatters, duplicate-key guard, and the "key in every active locale" rule are all built locale-count-agnostic, so the follow-up import adds no runtime change and no new WP-scale work.
  - *Consequence:* the "all 5 locales" acceptance/PR discipline reduces to "both active locales (en, de)" for WP03; it re-expands automatically when FR/ES/IT land.
- **D-P3-I18N-LIB — i18n library. APPROVED: keep `i18next` + `react-i18next`.** The salvage verdict is explicit ("i18next + 5-locale corpus = Keep"); the corpus keys, the `t(...)` call sites in every future ported page, and the duplicate-key check are all i18next-shaped. Reimplementing on a different runtime would strand the wholesale corpus. New production dependencies recorded here before use: `i18next`, `react-i18next`.
  - *Alternative:* a lighter custom message runtime — rejected (throws away the salvage value and the mandated build guard).
- **D-P3-I18N-PERSIST — language preference storage + sign-out interaction. APPROVED: persist the language choice in `localStorage` under a namespaced key (e.g. `ll.language`), and explicitly EXEMPT it from the WP02 sign-out teardown.** Rationale: a UI **language** is a device/browser preference, not space- or identity-scoped "app state"; the §8.1 "sign-out clears all app state (kills the stale-space auto-join class)" requirement targets cached space/identity data (the TanStack Query cache + MSAL cache), not the display language. Keeping it in `localStorage` (not `sessionStorage`) lets the choice survive tab close, matching OLD behavior; the exemption is documented so a QA reviewer does not read it as a teardown gap.
  - *Alternative:* store language in `sessionStorage` and clear it on sign-out → the language resets to `en` on every sign-out and tab close (worse UX, no security benefit — language is not sensitive). Rejected unless the user wants strict "clear everything".
- **D-P3-I18N-FORMAT — formatting source. APPROVED: format at the render edge with `Intl.*` keyed to the ACTIVE i18n locale only; money is integer-safe (no float).** The OLD space-preference override machinery is deferred to Phase 4 (see Non-goals). `formatMoney` must not do float division; correctness is pinned by unit tests including large values and 0-/3-decimal currencies.
  - *Alternative:* port the OLD `NumberFormatSetting`/`DateFormatSetting` space-preference overrides now → pulls in the Cosmos/space-preference data path that §5 says to rewrite, and depends on an admin surface that does not exist yet. Rejected for WP03.
- **D-P3-I18N-CORPUS — corpus fidelity. APPROVED: import the locale JSONs verbatim, including known OLD content defects; correcting translations is out of scope.** Preserves the salvage oracle and avoids unreviewed accounting-vocabulary drift; the duplicate-key structural check is the only guard applied. (Applies to EN + DE now, and to FR/ES/IT when the deferred follow-up imports them.)

## Accounting decisions

**None.** Internationalization and render-edge formatting are presentation plumbing, not accounting behavior. No LL Accounting Expert consult. **One adjacent invariant is respected, not decided:** money is integer minor units + ISO currency and must be formatted with **no float arithmetic** — this is the existing SharedKernel/frontend rule (P1-WP03, frontend.instructions.md), enforced here by the `formatMoney` acceptance tests, not a new accounting decision.

## Golden fixtures

**None required.** WP03 ports no accounting rule and produces no financial computation — `formatMoney` is presentation, not arithmetic on amounts (it never changes a value, only renders it). Its correctness (integer-safe, locale-correct, currency-decimal-correct) is pinned by **unit tests** (the correct tier), not golden fixtures. Client-side *validation* mirroring (fixture-pinned) begins in **WP06**, not here.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Verbatim salvage (copied, adapted only for path/wiring)
- **`src/i18n/locales/en.json` + `src/i18n/locales/de.json`** → `app/src/i18n/locales/{en,de}.json` — byte-for-byte, no content change. (`fr.json`/`es.json`/`it.json` deferred per D-P3-I18N-LOCALES.)
- **`src/i18n/index.ts`** (i18next init shape: `initReactI18next`, `resources`, `fallbackLng:'en'`, `escapeValue:false`, `setLanguage`) → `app/src/i18n/index.ts`, adapted for the persistence-key rename (D-P3-I18N-PERSIST) and the reduced two-locale resource map.
- **`scripts/check-i18n-duplicate-keys.cjs`** → `tools/check-i18n-duplicate-keys.cjs`, adapted to resolve the new locale path and run from `app/`.

### Rewrite / explicit non-port
- **Render-edge formatters** (`formatMoney`/`formatDate`/`formatNumber` + hooks) — greenfield, locale-keyed, integer-safe money; the OLD `useNumberFormat`/`useDateFormat`/`spacePreferencesApi` (space-preference-driven, Dexie/Cosmos-coupled) are an explicit **non-port** (Phase 4).
- **Shell string replacement** — WP01/WP02 placeholders → keys; the OLD `AppLayout`/`SettingsPage` chrome is a *reference* for key names, not a code port (the OLD shell is Dexie/rights-coupled).

## Dependencies

- **New production dependencies (frontend, recorded here before use):** `i18next`, `react-i18next` (D-P3-I18N-LIB). No other new production deps. Any addition beyond these is a plan amendment.
- **No new backend dependency**, no new tooling dependency (the duplicate-key checker is dependency-free Node, matching the OLD script).
- Existing Vitest + Testing Library stack (WP01) covers the tests. No E2E/Playwright in WP03.

## File list (implementation target — flat `app/src`, per approved D-P3-STRUCT)

**New — `app/src/i18n/`**
- `locales/en.json`, `locales/de.json` — verbatim OLD corpus (the two launch locales; FR/ES/IT deferred per D-P3-I18N-LOCALES).
- `index.ts` — i18next init (two-locale resource map) + `setLanguage` + supported-locale list + persistence (D-P3-I18N-PERSIST).
- `format/money.ts` — `formatMoney(minorUnits, currency, locale)` (integer-safe).
- `format/datetime.ts` — `formatDate` / `formatNumber` locale wrappers.
- `hooks.ts` — `useMoneyFormat` / `useDateFormat` / `useNumberFormat` (bind active locale).

**New — `tools/`**
- `check-i18n-duplicate-keys.cjs` — ported duplicate-key build guard.

**Modified**
- `app/src/app/providers.tsx` — wrap the tree in `I18nextProvider` (outermost).
- `app/src/app/AppLayout.tsx`, `HomeRoute.tsx`, `NotFoundRoute.tsx`, `AppErrorBoundary.tsx`, `RouteErrorBoundary.tsx` — replace hard-coded strings with `t(...)` keys.
- `app/package.json` — add `i18next`/`react-i18next`; add `i18n:keys:check` script; prepend it to `build`.
- `app/src/main.tsx` — import the i18n init module for its initialization side effect (before render), if not imported transitively via `providers`.
- CI workflow (`.github/workflows/pr.yml` / `main.yml`) — only if an explicit `i18n:keys:check` step is desired in addition to its inclusion in `build`; otherwise unchanged (build already runs it). Document the choice.
- `docs/rebuild/plans/P3-WP03-i18n-corpus-import.md` + `docs/rebuild/status.md` — notes/state.

**New — tests (`app/src/**` colocated or `app/tests/`)**
- i18n init test (both locales load; `fallbackLng` en; `t('common.save')` resolves for en + de; `setLanguage('de')` changes active language + persists; an unknown language falls back to en).
- Locale-file integrity test (both files exist, parse as objects; `en`/`de` present).
- Duplicate-key guard test/red-gate proof (checker fails on an injected duplicate, passes clean).
- `formatMoney` unit test (integer-safe: negative, zero, positive; 2-decimal, 0-decimal JPY, 3-decimal BHD; a large value; asserts no `NaN`/float artifact and locale-correct grouping/symbol; a grep/test asserts the impl contains no `/ 100`-style float division).
- `formatDate`/`formatNumber` locale tests (en vs de output differs as expected).
- Shell-i18n test (rendered shell/splash/404/error fallback show translated copy; a grep/test asserts no remaining hard-coded user-facing string in `app/src/app/**`).
- Language-persistence test (choice survives a simulated reload; a sign-out teardown / `queryClient.clear()` does NOT clear the language key — D-P3-I18N-PERSIST).

No changes under `app/src/api/**` (generated), no backend files, no `backend/openapi/**` regeneration, no new migration.

## Acceptance criteria (concrete, testable)

1. **Both launch locales load.** i18next initializes with `en` and `de` under `translation`, `fallbackLng: 'en'`, `escapeValue: false`; a test asserts each language resolves a known key (e.g. `common.save`) to its locale value and an unknown language falls back to `en`. (FR/ES/IT are intentionally absent — D-P3-I18N-LOCALES.)
2. **Corpus is verbatim + valid.** The `en.json` and `de.json` files exist and parse as objects; a test (or a committed hash/manifest note) confirms they match the OLD `src/i18n/locales/{en,de}.json` content (no keys added/removed by WP03 beyond the shell keys, which are additive and present in both).
3. **Duplicate-key guard is blocking.** `npm run build` runs the duplicate-key check and **fails** (non-zero) when a duplicate JSON key is injected into any locale file, and passes when clean — proven by a red-gate demonstration (inject → build fails → revert → build passes).
4. **Provider mounted shell-wide.** `I18nextProvider` wraps the app above the router; the initialization splash, the auth chrome, both error-boundary fallbacks, and the home/404 routes all render translated copy — proven by a render test.
5. **No hard-coded shell copy remains.** `AppLayout`, `providers` splash, `HomeRoute`, `NotFoundRoute`, and both error boundaries use `t('…')`; a grep/test asserts no residual hard-coded user-facing English string in `app/src/app/**` (the WP01/WP02 temporary placeholders are gone) and that each new key exists in **both active** locale files (`en`, `de`).
6. **`formatMoney` is integer-safe and locale/currency correct.** A unit test asserts correct output for negative/zero/positive amounts across a 2-decimal currency, a 0-decimal currency (JPY), and a 3-decimal currency (BHD), with locale-correct grouping and symbol; a source-level assertion (grep/test) confirms the implementation performs **no float division** on the minor units (no `/ 100`, no `Number(minor)/…`). A large value formats without precision loss artifacts.
7. **Locale-keyed date/number formatting.** `formatDate`/`formatNumber` (and their hooks) produce locale-correct output that differs between `en` and `de` for the same input — proven by tests; they read the active i18n language, not a hard-coded locale.
8. **Language preference persists and is teardown-exempt.** A selected language persists across a simulated reload (via the `localStorage` key), and a simulated WP02 sign-out teardown (`queryClient.clear()` + auth state reset) does **not** clear the language preference — proven by a test (D-P3-I18N-PERSIST).
9. **Boundaries + page budget green; generated client untouched.** ESLint boundary rules stay green with `app/src/i18n/**` populated (no rule loosened); `npm run check:page-budget` passes for every new `.ts/.tsx` file; `app/src/api/**` is byte-unchanged; no backend/OpenAPI change (CI `contract` gate unaffected).
10. **All gates green.** `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`, and `npm run build` are green; `npm audit --omit=dev` shows no new vulnerabilities from `i18next`/`react-i18next`.

## Boundary note

WP03 respects the frontend layering (**features → application → api**) and adds a cross-cutting **shared** module (`app/src/i18n/**`) that is a leaf: it imports nothing from `features`, `app`, or `application`, and may be imported by any of them (translation + formatting are presentation utilities, not data access — no `fetch`, no generated-client call). No feature bypasses the generated client; no data path is added. The new production dependencies (`i18next`, `react-i18next`) are recorded above before use per the no-undocumented-dependency rule.

## Open questions / carry-forwards

- **D-P3-I18N-LIB / -PERSIST / -FORMAT / -CORPUS / -LOCALES** — all approved by the user 2026-07-14; now implementation constraints. EN + DE ship in WP03; FR/ES/IT deferred.
- **FR/ES/IT follow-up (deferred, D-P3-I18N-LOCALES)** — a later content-only task imports the OLD `fr.json`/`es.json`/`it.json` verbatim and registers them in the `resources` map + supported-locale list; no runtime/formatter change. The known `it.json` Spanish-string defect is corrected (or accepted) as part of that task, not WP03. Propose as a small follow-up WP (e.g. **P3-WP03b** or a Phase-4 content item) when the primary shell/pages are green.
- **Space-preference formatting (Phase 4)** — the admin-set number/date/accent/fiscal preferences and their persistence are deferred; when that surface lands, the render-edge hooks delivered here are the seam it overrides. Recorded so a later WP does not duplicate the formatters.
- **Language-switcher UI** — `setLanguage()` + the supported-locale list exist here; the picker lands with a settings/preferences page (WP05+ / Phase 4).
- **Corpus content defects** — the EN/DE launch files have no known such defect; the OLD `it.json` Spanish-string defect is a concern only for the deferred FR/ES/IT follow-up, which ports verbatim and handles a content-quality pass separately (not WP03).
- **Money precision beyond 2^53** — amounts arrive typed as `number` (integer minor units) in the generated schema; if a future amount can exceed `Number.MAX_SAFE_INTEGER`, the contract should carry it as a string and `formatMoney` should accept `string | number | bigint`. Flagged for the WP05/06 planners; WP03's `formatMoney` should accept a `number` today but be written so a `string`/`bigint` overload is a non-breaking addition.

## Implementation log

- **2026-07-14 — LL Frontend Dev:** Implemented the EN/DE i18n runtime with `I18nextProvider`, namespaced `ll.language` persistence, shell translation keys, integer-safe money formatting, locale date/number wrappers and hooks, plus the duplicate-key build gate. Imported the full local OLD EN/DE corpus from `C:\Programming\LeafLedger\Accounting\src\i18n\locales\`, preserving its content and adding only the WP03 shell keys required by the new app shell. Added focused runtime/formatter/shell coverage, scoped duplicate-key regression probes, and approved `i18next`/`react-i18next` dependencies. FR/ES/IT remain deferred.
- **Validation:** focused runtime/formatter/shell tests **11/11** and full Vitest **28/28** pass. Duplicate-key check, scoped duplicate-key red-gate probes, typecheck, lint, strict page budget, production build, `git diff --check`, and npm audit (**0 vulnerabilities**) pass. State remains `verify`; next: LL QA Reviewer acceptance review.

## QA verdict

**PASS — 2026-07-14 — LL QA Reviewer**

1. All ten WP03 acceptance criteria are met. The i18next runtime and outermost provider are correctly wired; EN/DE load with English fallback; shell, route, auth, splash, and error-boundary copy uses translation keys; language persistence survives query-cache teardown; and the render-edge formatters are locale-aware and integer-safe.
2. Corpus fidelity was independently checked against `C:\Programming\LeafLedger\Accounting\src\i18n\locales\`: both current files preserve every OLD key/value and add only the documented WP03 keys (`nav.*`, `auth.notConfigured`, and `shell`) in both locales. FR/ES/IT remain intentionally deferred.
3. The duplicate-key parser is scope-aware: repeated leaf names in separate objects pass, while duplicate keys in one object fail non-zero. The clean gate and production build both pass.
4. Frontend validation: Vitest **28/28**, typecheck, lint, strict page budget, duplicate-key gate, production build, `git diff --check`, and npm audit (**0 vulnerabilities**) all pass. No changes were found under `app/src/api/**`, `backend/**`, or `backend/openapi/**`.
5. Financial/security/hallucination/patch-layering review found no WP03 issue: no money float division, no data-access bypass, no endpoint or authorization surface, no secrets, and no generic catch/self-healing layer.
6. Repository-wide backend regression was attempted but could not execute its Testcontainers tier because Docker was unavailable; it produced **203 passed / 129 setup failures** from `Docker EndpointAuthConfig`. This is an environment limitation outside WP03 scope, not a WP03 finding; no backend files were changed.

**State:** `done`
