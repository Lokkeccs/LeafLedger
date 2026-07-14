# P3-WP04 — Shared UI primitives + design tokens (DataTable, modal, form primitives, generic pickers)

- **Phase:** 3 (frontend re-platform)
- **State:** done — QA PASS on 2026-07-14.
- **Owner (implementation):** LL Frontend Dev
- **Depends on:**
  - **P3-WP01** (done) — the app-shell provider tree, desktop-first `AppLayout`, and the ad-hoc CSS-custom-property set currently living in `app/src/index.css` (`--ink`, `--muted`, `--paper`, `--surface`, `--line`, `--accent`, `--focus`, `--shadow`). WP04 formalizes those into a canonical token layer the primitives reference.
  - **P3-WP03** (done) — the render-edge formatting layer (`formatMoney`, `formatDate`, `formatNumber` + `useMoneyFormat`/`useDateFormat`/`useNumberFormat` hooks) and the i18n runtime. The rewritten integer-safe money input and any primitive that renders copy consume these; WP04 adds **no** new formatting or locale logic.
- **Blocks:** the Phase-3 vertical-slice pages — **WP05** (read-only accounts page → `DataTable`), **WP06** (journal-entry posting flow → form primitives, money input, generic date field, modal), **WP07** (trial-balance report page → `DataTable`). "Reuse shared primitives before writing new UI" (frontend rules, playbook §107) is only enforceable once this library exists.
- **Estimated size:** ≤ 2 days (salvage a focused, high-leverage subset of the OLD `src/shared/` presentation primitives with the responsive-table machinery stripped, synthesize the canonical design-token layer, rewrite the money input to be integer-safe, colocated tests). **No backend, no OpenAPI/TS regeneration, no new API surface, no new production dependency expected.** If the whole exceeds ≤ 2 days, split at the documented WP04a/WP04b seam.

## Context / scope note (LL Architect, 2026-07-14)

Part 4 §Phase 3 bundles the whole frontend re-platform ("port `shared/` UI primitives"); Phase 3 was decomposed into 8 WPs (P3-WP01…08) on 2026-07-12. **WP04 is the shared-primitives + design-token slice only:** stand up the reusable, desktop-first, token-driven UI library that WP05–WP07 build their pages on. Authentication is WP02 (done), i18n/formatting is WP03 (done), the pages themselves are WP05–WP07, live invalidation is WP08. WP04 adds **no** page (no `*Page.tsx`), **no** endpoint call, and **no** data access — it is pure presentation.

This is a **salvage** WP per §5 ("React UI primitives … = salvage") and the §2 verdict ("React 19 + TS strict, react-router 7 = **Keep**; salvages UI primitives"). The OLD `src/shared/` directory is the source. Two OLD patterns are explicit **non-ports**, mandated by the desktop-first decision and the no-float rule:

1. **The responsive-table machinery** (`useTableBreakpoint.ts` / `useIsTightTableScreen` + `DataTable`'s `compactCard` branch) — frontend rules: *"Mobile responsiveness is **not** a requirement … do not port the old responsive-table machinery."* The salvaged `DataTable` ships **desktop-only** (the `matchMedia`/compact-card path removed).
2. **The OLD `AmountInput`** — it parses with `parseFloat` (float arithmetic on money — forbidden) and formats via the Cosmos space-preference `NumberFormatContext` (data-access-coupled — a §5 rewrite). WP04 ships a **rewritten** integer-minor-units money input built on WP03's integer-safe `formatMoney` — see D-P3-UI-AMOUNT.

Scope is deliberately bounded to the **foundation set** the vertical slice actually consumes (table, modal, form field primitives, integer-safe money input, a generic date field, a toggle, the token layer). **Domain-coupled pickers** (`AccountPicker`, business-partner pickers, recent-account memory) read ledger/chart data and therefore land with the pages that own that data (WP06), built on the generic primitives delivered here — see D-P3-UI-PICKERS. **Theme/dark-mode** (a space/user preference, like number format) is deferred to the Phase-4 admin surface — see D-P3-UI-THEME.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §2 salvage table ("React 19 + TS strict, react-router 7 = **Keep** — Proven; **salvages UI primitives**, feature policy, page structure"), §7 diagram row ("`shared/` (UI primitives, i18n ×5, formatting) … light TS pre-validation (UX only)"), §"Decision (2026-07-04): the full product is **desktop-first** … phones get a lightweight companion app, not feature parity".
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 ("New app shell … **port `shared/` UI primitives**; accounts + journal entry pages … decomposed under page budgets"), §5 salvage ("**React UI primitives**, feature-policy matrix, i18n corpus, MSAL flow shape … = salvage") vs rewrite ("**all data access** Dexie → TanStack Query + generated client; journal/import pages decomposed").
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 Shell ("i18n … **theming**, date/number format prefs" — theming and format prefs are the admin/space-preference surface, **Phase 4**, not WP04; WP04 delivers the **token layer** those preferences would later drive, plus a light default).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 ("**Reuse the salvaged shared primitives (DataTable, pickers, modals) before writing new UI**").
- `.github/instructions/frontend.instructions.md` — **Design system (desktop-first):** "do not port the old responsive-table machinery"; "**Reuse shared primitives before writing new UI** (DataTable, pickers, modals, form primitives)"; "Use **design tokens** (colors, spacing, radius, shadows via CSS custom properties) — never hard-code raw hex/px where a token exists. Tokens live in the shared design module (final path settles at the P3 re-platform; today: shared styles/tokens under `app/src`)." **Money:** "arrives as integer minor units + ISO currency; **format only at the render edge. Never do float math on amounts.**" **Layering:** features → application → api; page budget 450/30/20.
- `docs/rebuild/plans/P3-WP01-app-shell-foundation.md` — the desktop-first layout frame + the ad-hoc token set in `index.css` this WP formalizes.
- `docs/rebuild/plans/P3-WP03-i18n-corpus-import.md` — the render-edge formatters/hooks (`formatMoney`, `useMoneyFormat`, `useDateFormat`, `useNumberFormat`) the money input and copy-rendering primitives consume; the "shared leaf module" boundary precedent (`app/src/i18n/**` imports nothing from `features`/`app`/`application`).

## Goal

Establish the desktop-first, token-driven shared UI primitive library under `app/src/shared/` that the Phase-3 vertical-slice pages (WP05–WP07) build on, so that "reuse shared primitives before writing new UI" is enforceable. Concretely:

1. Synthesize a **canonical design-token layer** (`app/src/shared/styles/tokens.css`) — colours (brand/neutral/status), spacing scale, radii, shadows, typography — as CSS custom properties, using the WP01 brand aesthetic for values and the OLD structural scales (`--space-*`, `--radius-*`, `--shadow-*`) for shape, and migrate the WP01 shell's ad-hoc custom properties onto it with **no visual change**.
2. Salvage the **foundation primitive set** from OLD `src/shared/`, re-pointed to the canonical tokens and stripped of the responsive-table and space-preference machinery: `DataTable` (desktop-only) + its `tableStyles`/`listPrimitives` support, `ModalShell`, `FormSection`, `ToggleSwitch`, a generic `DateField`, and (optional/stretch) `useToast`.
3. **Rewrite** the money input as an integer-minor-units `MoneyInput` built on WP03's `formatMoney` — **no float arithmetic** — replacing the OLD float+space-preference `AmountInput`.
4. Expose the library through a small barrel (`app/src/shared/index.ts`) and keep `app/src/shared/**` a true **leaf** (imports nothing from `features`/`app`/`application`/`api`).

WP04 renders no page, calls no endpoint, and adds no data path.

## Scope

1. **Design tokens (synthesis) — `app/src/shared/styles/tokens.css`:**
   - Define the canonical `:root` custom-property set the primitives reference: brand/accent + neutral surface/ink/line/muted (values from the WP01 `index.css` aesthetic so the shell's appearance is unchanged), a status palette (`--color-success`/`--color-warning`/`--color-danger`/`--color-info` + subtle backgrounds), a spacing scale (`--space-1..5`), radii (`--radius-sm/md/lg`), shadows (`--shadow-sm/md/strong`), and typography vars. Names follow the OLD vocabulary (`--color-*`, `--space-*`, `--radius-*`, `--shadow-*`) so salvaged primitives referencing them need no rewrite.
   - Import `tokens.css` once (from `app/src/index.css` via `@import` at the top, or from `main.tsx`); **migrate** the WP01 ad-hoc props (`--ink`/`--paper`/`--surface`/`--line`/`--accent`/`--focus`/`--shadow`) to reference/alias the canonical tokens so there is a single source of truth and **no visual regression** (the shell renders pixel-equivalent). See D-P3-UI-TOKENS.
2. **`DataTable` (salvage, desktop-only) — `app/src/shared/DataTable.tsx` + `tableStyles.ts` + `listPrimitives.tsx`:**
   - Port the OLD generic `DataTable<T>` (typed `ColumnDef<T>` = data/actions columns, `colgroup` widths, three-state gate: empty / no-match / rows, `onRowClick`, `rowKey`, `ariaLabel`) **with the responsive branch removed** — drop the `useIsTightTableScreen`/`compactCard` path entirely (D-P3-UI-RESPONSIVE). Port `tableStyles.ts` (`thStyle`/`tdStyle`) re-pointed to tokens and `listPrimitives.tsx` (`ActionCell`) as its support.
   - **Do not** port `useTableBreakpoint.ts` / `useIsTightTableScreen` — explicit non-port.
3. **`ModalShell` (salvage) — `app/src/shared/ModalShell.tsx` + `ModalShell.module.css`:**
   - Port the `createPortal` overlay primitive (backdrop-click + built-in close, `role="dialog"`/`aria-modal`/`aria-labelledby`, header/body/footer slots, `maxWidth`/`zIndex`/`center`). Re-point the CSS module to canonical tokens. Copy strings are caller-supplied (i18n keys), not hard-coded.
4. **Form primitives (salvage + one rewrite) — `app/src/shared/`:**
   - `FormSection.tsx` — salvage verbatim (already token-driven).
   - `ToggleSwitch.tsx` + `ToggleSwitch.module.css` — salvage, re-pointed to tokens.
   - `FormField.tsx` — a thin label + control + error-slot wrapper (small greenfield primitive; the OLD app had ad-hoc field markup — this consolidates it) so WP06's posting form has a consistent field shell.
   - `MoneyInput.tsx` — **rewrite** of the OLD `AmountInput`: value is **integer minor units** (`number`) + an ISO currency; display uses WP03 `formatMoney` when unfocused and a raw editable string while focused; parsing user input to minor units is **integer/string arithmetic only — no `parseFloat`, no `/100`, no float multiplication that can lose cents**; handles negative/zero, 2-decimal, 0-decimal (JPY), 3-decimal (BHD) currencies via the currency's fraction-digit count. **Does not** import the OLD `NumberFormatContext`/space-preference machinery. See D-P3-UI-AMOUNT.
5. **Generic date field (salvage, presentation-only) — `app/src/shared/DateField.tsx`:**
   - A thin, presentation-only date input (native `<input type="date">` wrapped with label/token styling and ISO `YYYY-MM-DD` value semantics) that WP06's journal-entry effective-date field consumes. The OLD `SingleDatePickerControl`/`DateFilterPicker`/`ReportingDateRangePickerControl` are references for shape; the full range-picker/preset machinery (`savedDatePresets`, `useSavedDatePresets`, `DateFilterContext`) is **not** ported here (reporting-filter surface → later). See D-P3-UI-PICKERS.
6. **Toast (optional/stretch) — `app/src/shared/useToast.tsx`:**
   - Port the OLD toast primitive if it fits the budget; otherwise defer to the first page that needs transient feedback (WP06). Marked optional so it never blocks the WP.
7. **Barrel + boundary + budget conformance — `app/src/shared/index.ts`, `eslint.config.js`:**
   - Export the primitives from `app/src/shared/index.ts`. Keep `app/src/shared/**` a leaf (imports nothing from `features`/`app`/`application`/`api`; no `fetch`, no generated-client call). Optionally add a lightweight ESLint boundary rule pinning that direction (D-P3-UI-BOUNDARY). The page-budget tool only targets `src/app/App.tsx` and `src/features/**/*Page.tsx`, so shared primitives are not budget-counted; each nonetheless stays small and single-purpose.

## Non-goals (explicitly deferred)

- **No responsive/mobile-table machinery — non-port (desktop-first).** `useTableBreakpoint`/`useIsTightTableScreen` and the `DataTable` `compactCard` branch are dropped. The future phone companion app is a separate client.
- **No space-preference-driven formatting — Phase 4.** The OLD `NumberFormatContext`/`DateFormatContext`/`useNumberFormat`/`useDateFormat`/`spacePreferencesApi` (admin-set `dateFormat`/`numberFormat`, Cosmos-synced) are **not** ported. Primitives format via the WP03 active-locale render-edge hooks only.
- **No theming / dark-mode runtime — Phase 4.** `ThemeContext`/`useTheme` (accent colour + light/dark, a space/user preference) is deferred. WP04 ships the token layer with a light default; the token file may optionally carry an inert `[data-theme="dark"]` block for a future switcher, but no theme toggle/runtime is wired. See D-P3-UI-THEME.
- **No domain-coupled pickers — WP06.** `AccountPicker`/`accountPickerFilter`/`useRecentAccounts`, business-partner pickers, and any control that reads ledger/chart data land with the journal-entry page (WP06), built on the generic primitives here. See D-P3-UI-PICKERS.
- **No reporting date-range/preset machinery — later.** Range pickers, saved presets, `DateFilterContext`, `FilterBar`, `TopTabs`, `ViewSwitcher` are report/list-filter surfaces ported when their pages land (WP07/Phase 4), not in the foundation set.
- **No document-capture / camera primitives — Phase 4.** `CameraModal` (Documents/attachments) is out of scope.
- **No new page (`*Page.tsx`), no endpoint call, no data access.** WP04 is presentation-only.
- **No backend change, no OpenAPI/TS regeneration.** `app/src/api/**` is byte-unchanged; the CI `contract` gate is unaffected.

## Decisions (front-loaded, non-accounting — all APPROVED by the user 2026-07-14 on their recommended routes)

Per the WP01/WP02/WP03/WP12/WP13 precedent, load-bearing structural choices with no accounting content are surfaced for a one-time user decision so implementation is unambiguous. The user approved all six recommendations 2026-07-14; each is now an implementation constraint.

- **D-P3-UI-TOKENS — token vocabulary & shell migration. APPROVED (recommended route): one canonical `app/src/shared/styles/tokens.css` using OLD structural token names (`--color-*`/`--space-*`/`--radius-*`/`--shadow-*`) with values drawn from the WP01 brand aesthetic; migrate the WP01 shell's ad-hoc `index.css` props onto it with no visual change.** A single source of truth lets salvaged primitives keep their `var(--space-3)`/`var(--radius-md)` references unchanged while the shell keeps its look. The migration is mechanical (alias the existing props to canonical tokens); a render/visual-smoke assertion guards against regression.
  - *Alternative:* keep the WP01 props and the OLD token names as two parallel sets → duplicated truth, "which token do I use?" ambiguity. Rejected.
- **D-P3-UI-RESPONSIVE — table breakpoint. APPROVED (recommended route): drop `useTableBreakpoint`/`useIsTightTableScreen` and the `DataTable` `compactCard` branch; ship `DataTable` desktop-only.** Directly mandated by the desktop-first decision and frontend rules. Stated explicitly so the salvaged `DataTable` diff (removed branch) is not read as an accidental omission.
  - *Alternative:* port the responsive branch → violates the frontend rules. Rejected.
- **D-P3-UI-AMOUNT — money input. APPROVED (recommended route): rewrite as an integer-minor-units `MoneyInput` on WP03 `formatMoney`, no float, no space-preference context.** The OLD `AmountInput` does `parseFloat`/float math (forbidden) and reads Cosmos space preferences (a §5 rewrite). The rewrite is pinned by integer-safe unit tests (round-trip, 0-/2-/3-decimal currencies, large values, negative/zero).
  - *Alternative:* port `AmountInput` as-is → ships float arithmetic on money and a deferred data path. Rejected.
- **D-P3-UI-THEME — theming/dark-mode. APPROVED (recommended route): defer the theme runtime (`ThemeContext`/`useTheme`) to Phase 4; ship the token layer + light default only (optional inert dark-token block, no switcher).** Theme/accent is a space/user preference like number format (roadmap M1 admin surface), not part of the Phase-3 vertical slice. The token layer delivered here is the seam that preference later drives.
  - *Alternative:* port the theme context now → pulls in the space-preference data path (deferred) and an admin surface that does not exist yet. Rejected for WP04.
- **D-P3-UI-PICKERS — picker scope. APPROVED (recommended route): ship only generic presentation pickers here (a generic `DateField`); defer domain-coupled pickers (`AccountPicker`, business-partner, recent-account memory) and reporting range/preset pickers to the pages that own their data (WP06/WP07).** Keeps WP04 a true presentation leaf (no data access) and ≤ 2 days; the WP-row word "pickers" is honoured by the generic date field while the account/partner pickers correctly land where the account/partner data lands.
  - *Alternative:* port `AccountPicker` here → forces a data dependency (accounts endpoint/seed, which is WP05) into the presentation library. Rejected.
- **D-P3-UI-BOUNDARY — shared-leaf lint rule. APPROVED (recommended route): add a lightweight ESLint rule making `app/src/shared/**` a strict leaf (must not import from `features`/`app`/`application`/`api`), matching the intent of the WP03 i18n leaf.** Cheap architectural insurance as the library grows and every feature imports it. If the user prefers minimal config, follow the WP03 precedent (no new rule) and rely on review — recorded either way.
  - *Alternative:* no rule (WP03 i18n precedent) → acceptable but weaker guarantee. Recommend adding the rule.

## Accounting decisions

**None.** Shared UI primitives and design tokens are presentation plumbing, not accounting behavior. No LL Accounting Expert consult. **One adjacent invariant is respected, not decided:** money is integer minor units + ISO currency and must be rendered/parsed with **no float arithmetic** — the existing SharedKernel/frontend rule (P1-WP03, WP03, frontend.instructions.md), enforced here by the `MoneyInput` unit tests, not a new accounting decision.

## Golden fixtures

**None required.** WP04 ports no accounting rule and performs no financial computation. `MoneyInput` renders via the already-pinned WP03 `formatMoney` and parses UX input to minor units with integer-safe arithmetic; its correctness (integer-safe round-trip, currency-decimal-correct, no float artifact) is pinned by **unit tests** (the correct tier), not golden fixtures. Client-side **validation mirroring** (balance/currency rules pinned by the P2-WP01/WP04 golden fixtures) begins in **WP06**, not here.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout at `C:\Programming\LeafLedger\Accounting`).

### Verbatim / near-verbatim salvage (copied, re-pointed to canonical tokens, responsive branch stripped)
- `src/shared/DataTable.tsx` → `app/src/shared/DataTable.tsx` — **remove** the `useIsTightTableScreen`/`compactCard` responsive path (D-P3-UI-RESPONSIVE); keep the typed columns + three-state gate.
- `src/shared/tableStyles.ts` → `app/src/shared/tableStyles.ts`; `src/shared/listPrimitives.tsx` (`ActionCell`) → `app/src/shared/listPrimitives.tsx`.
- `src/shared/ModalShell.tsx` + `src/shared/ModalShell.module.css` → `app/src/shared/ModalShell.{tsx,module.css}`.
- `src/shared/FormSection.tsx` → `app/src/shared/FormSection.tsx` (already token-driven).
- `src/shared/ToggleSwitch.tsx` + `src/shared/ToggleSwitch.module.css` → `app/src/shared/ToggleSwitch.{tsx,module.css}`.
- `src/index.css` `:root` token block (`--color-*`, `--space-*`, `--radius-*`, `--shadow-*`, `--font`) → **structural reference** for the canonical `tokens.css` vocabulary (names salvaged; **values** taken from the WP01 brand aesthetic, not the OLD teal palette — the visual identity is WP01's, not a re-theme).
- (Optional) `src/shared/useToast.tsx` → `app/src/shared/useToast.tsx`.

### Rewrite / explicit non-port
- `src/shared/AmountInput.tsx` → **rewritten** as `app/src/shared/MoneyInput.tsx` (integer minor units + WP03 `formatMoney`; no `parseFloat`/float; no `NumberFormatContext`). Explicit non-port of the OLD float+space-preference implementation.
- `src/shared/useTableBreakpoint.ts` / `useIsTightTableScreen` — **non-port** (desktop-first).
- `src/shared/NumberFormatContext.tsx`, `DateFormatContext.tsx`, `useNumberFormat.ts`, `useDateFormat.ts`, `numberFormatContextValue.ts`, `useNumberFormatContext.ts`, `view-model/spacePreferencesApi.ts` — **non-port** (space-preference/data-access-coupled; superseded by the WP03 render-edge hooks; Phase-4 admin surface).
- `src/shared/ThemeContext.tsx`, `useTheme.ts` — **non-port** here (Phase-4 theming; D-P3-UI-THEME).
- `src/shared/AccountPicker*.{tsx,css,ts}`, `useRecentAccounts.ts`, business-partner pickers — **deferred to WP06** (domain/data-coupled; D-P3-UI-PICKERS).
- `src/shared/DateFilterPicker*`, `ReportingDateRangePickerControl.tsx`, `DateRangeTriggerField*`, `savedDatePresets.ts`, `useSavedDatePresets.ts`, `FilterBar*`, `TopTabs*`, `ViewSwitcher*`, `CameraModal.tsx` — **deferred** (report/list-filter or capture surfaces; later WPs / Phase 4).

## Dependencies

- **No new production dependency expected.** The primitives are React + `react-dom` (`createPortal`, already present) + CSS. If a specific salvage forces a new dep, it is a plan amendment recorded here before use — the default expectation is **zero new deps** (a selling point vs the OLD stack).
- **No new backend dependency**, no new tooling dependency.
- Existing Vitest + Testing Library stack (WP01/WP03) covers the tests; Vite's built-in CSS-modules support covers the `.module.css` files. No E2E/Playwright in WP04.

## File list (implementation target — flat `app/src`, per approved D-P3-STRUCT)

**New — `app/src/shared/`**
- `styles/tokens.css` — canonical design-token layer (D-P3-UI-TOKENS).
- `DataTable.tsx`, `tableStyles.ts`, `listPrimitives.tsx` — desktop-only table + support.
- `ModalShell.tsx`, `ModalShell.module.css` — overlay primitive.
- `FormSection.tsx` — section wrapper.
- `FormField.tsx` — label + control + error-slot field shell.
- `MoneyInput.tsx` — integer-safe money input (rewrite of `AmountInput`).
- `DateField.tsx` — generic presentation date input.
- `ToggleSwitch.tsx`, `ToggleSwitch.module.css` — toggle.
- `useToast.tsx` — (optional/stretch) transient feedback.
- `index.ts` — barrel exporting the public primitives.

**Modified**
- `app/src/index.css` — `@import` the token layer; migrate the WP01 ad-hoc custom props onto the canonical tokens (no visual change).
- `eslint.config.js` — add the `app/src/shared/**` leaf boundary rule (if D-P3-UI-BOUNDARY approved).
- `docs/rebuild/plans/P3-WP04-shared-ui-primitives.md` + `docs/rebuild/status.md` — notes/state.

**New — tests (`app/src/shared/**` colocated or `app/tests/`)**
- `DataTable` test: empty-state, no-match-state, rows render; typed data + actions columns; `onRowClick`; `ariaLabel`; a source/behavior assertion that **no** responsive/`matchMedia`/`compactCard` path exists (D-P3-UI-RESPONSIVE).
- `ModalShell` test: renders via portal when open, nothing when closed; backdrop mousedown+click closes; built-in close button fires `onClose`; `role="dialog"`/`aria-modal`/`aria-labelledby` present.
- `MoneyInput` test: integer-safe round-trip (type "12.34" → `1234` minor units; blur re-renders via `formatMoney`); negative/zero; 2-decimal, 0-decimal (JPY), 3-decimal (BHD); a large value with no precision-loss artifact; a source-level assertion the impl performs **no float division/`parseFloat`** on minor units.
- `FormSection`/`FormField`/`ToggleSwitch`/`DateField` render tests (label wiring, `aria`, controlled value/onChange).
- Token-layer test: `tokens.css` defines the documented token set; the shell still resolves its custom properties (a smoke assertion the migration introduced no undefined `var(--…)` in shell classes).
- Boundary/budget conformance: ESLint stays green with `app/src/shared/**` populated (leaf rule if added); `npm run check:page-budget` passes (no new `*Page.tsx`); `app/src/api/**` byte-unchanged.

No changes under `app/src/api/**` (generated), no backend files, no `backend/openapi/**` regeneration, no new migration.

## Acceptance criteria (concrete, testable)

1. **Canonical token layer exists and the shell is visually unchanged.** `app/src/shared/styles/tokens.css` defines the documented custom-property set (brand/neutral/status colours, `--space-1..5`, `--radius-sm/md/lg`, `--shadow-sm/md/strong`, typography); it is imported once; the WP01 shell props are migrated onto it; a test/smoke assertion confirms shell classes resolve their `var(--…)` (no undefined token) and the migration is value-preserving (no visual regression). (D-P3-UI-TOKENS)
2. **`DataTable` is desktop-only and correct.** The salvaged `DataTable<T>` renders the empty-state when `data` is empty, the no-match-state when `data` is non-empty but `rows` is empty, and the table (with `colgroup`, typed data + actions columns, `onRowClick`, `ariaLabel`) otherwise — proven by tests; a source/behavior assertion confirms **no** `useTableBreakpoint`/`useIsTightTableScreen`/`compactCard`/`matchMedia` path remains. (D-P3-UI-RESPONSIVE)
3. **`ModalShell` overlay works and is accessible.** Renders into a portal when open and nothing when closed; backdrop click and the built-in close button both call `onClose`; `role="dialog"`, `aria-modal="true"`, and `aria-labelledby` (when a titled) are present — proven by tests.
4. **`MoneyInput` is integer-safe and currency-correct.** A user typing a major-unit string produces the correct **integer minor-unit** value and blur re-renders via WP03 `formatMoney`; tests cover negative/zero/positive across a 2-decimal currency, a 0-decimal currency (JPY), and a 3-decimal currency (BHD), plus a large value with no precision-loss artifact; a source-level assertion confirms the implementation performs **no `parseFloat`/float division** on the minor units. (D-P3-UI-AMOUNT)
5. **Form primitives render and are controlled.** `FormSection`, `FormField` (label + control + error slot), `ToggleSwitch`, and `DateField` render with correct `aria`/label wiring and controlled value/onChange semantics — proven by tests; copy is caller-supplied (no hard-coded user-facing strings in the primitives).
6. **Library is a true leaf; boundaries stay green.** `app/src/shared/**` imports nothing from `features`/`app`/`application`/`api`, contains no `fetch`/generated-client call; ESLint (including the leaf rule if D-P3-UI-BOUNDARY is approved) is green; the public primitives are exported from `app/src/shared/index.ts`.
7. **No responsive/space-preference/theme machinery ported.** A repo assertion confirms none of `useTableBreakpoint`, `NumberFormatContext`, `DateFormatContext`, `useNumberFormat`, `useDateFormat`, `ThemeContext`, `useTheme`, or `AccountPicker` was introduced under `app/src/shared/**` (the documented non-ports/deferrals).
8. **Generated client untouched; no backend/contract change.** `app/src/api/**` is byte-unchanged; no backend/OpenAPI change; the CI `contract` gate is unaffected; `npm run check:page-budget` passes (no new `*Page.tsx`).
9. **No new production dependency (or one documented).** `app/package.json` gains no new runtime dependency; if one proves unavoidable it is recorded in this plan before use; `npm audit --omit=dev` shows no new vulnerabilities.
10. **All gates green.** `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`, and `npm run build` are green.

## Boundary note

WP04 respects the frontend layering (**features → application → api**) and populates the cross-cutting **shared** leaf module (`app/src/shared/**`): it imports nothing from `features`, `app`, `application`, or `api`, adds no `fetch` and no generated-client call, and may be imported by any feature/application/app file (UI primitives and tokens are presentation, not data access). This mirrors the WP03 `app/src/i18n/**` leaf precedent. If D-P3-UI-BOUNDARY is approved, a lightweight ESLint rule pins the leaf direction; otherwise the direction is review-enforced per the WP03 precedent. No new production dependency is expected; any addition is recorded above before use.

## Open questions / carry-forwards

- **D-P3-UI-TOKENS / -RESPONSIVE / -AMOUNT / -THEME / -PICKERS / -BOUNDARY** — all approved by the user 2026-07-14 on their recommended routes; now implementation constraints.
- **Theming / dark-mode (Phase 4)** — `ThemeContext`/`useTheme` + accent-colour/theme space preferences are deferred; the token layer delivered here is the seam that preference later drives (the optional inert `[data-theme="dark"]` block is where the values would live). Recorded so a later WP does not duplicate the token layer.
- **Space-preference formatting (Phase 4)** — the OLD `NumberFormatContext`/`DateFormatContext` overrides remain non-ported; `MoneyInput`/`DateField` format via the WP03 active-locale render-edge hooks, which are the seam the admin preference later overrides.
- **Domain-coupled pickers (WP06)** — `AccountPicker`/business-partner/recent-account controls land with the journal-entry page (they need the accounts source WP05 adds); they build on the generic primitives here. Recorded so WP06 reuses, not re-invents.
- **Reporting date-range/preset + list-filter primitives (WP07/Phase 4)** — range pickers, saved presets, `DateFilterContext`, `FilterBar`, `TopTabs`, `ViewSwitcher` are ported when their report/list pages land.
- **Money precision beyond 2^53** — amounts arrive typed as `number` (integer minor units) in the generated schema; `MoneyInput` accepts a `number` today but should be written so a `string`/`bigint` overload is a non-breaking addition (same flag carried from WP03). Confirm with the WP06 planner.
- **WP04a/WP04b split seam** — if the whole exceeds ≤ 2 days: **WP04a** = token layer + `DataTable` + `ModalShell` (what WP05/WP07 tables/modals need first); **WP04b** = form primitives + `MoneyInput` + `DateField` (what WP06's posting form needs). Documented so the ≤ 2-day rule is never breached.

## Implementation log

- **2026-07-14 — LL Architect:** Plan drafted → state `planned` (awaiting user approval). Researched §2 salvage verdict ("React 19 + TS = Keep; salvages UI primitives") and §7 diagram (`shared/` = UI primitives + i18n + formatting), §5 salvage/rewrite (React UI primitives = salvage; all data access = rewrite), the desktop-first decision + frontend rules (no responsive-table machinery; reuse shared primitives; design tokens via CSS custom properties; no float on money), roadmap M1 (theming/format-prefs = Phase-4 admin surface), and playbook §107. Inspected the local OLD checkout `src/shared/` with exact refs: salvage set (`DataTable.tsx` [strip `useIsTightTableScreen`/`compactCard`], `tableStyles.ts`, `listPrimitives.tsx`, `ModalShell.{tsx,module.css}`, `FormSection.tsx`, `ToggleSwitch.{tsx,module.css}`, optional `useToast.tsx`), non-ports (`useTableBreakpoint.ts`; `AmountInput.tsx` = float `parseFloat` + `NumberFormatContext` → **rewrite** as integer-safe `MoneyInput` on WP03 `formatMoney`; `NumberFormatContext`/`DateFormatContext`/`useNumberFormat`/`useDateFormat`/`spacePreferencesApi` = space-preference machinery; `ThemeContext`/`useTheme` = Phase-4 theming), and deferrals (`AccountPicker*`/`useRecentAccounts` → WP06; `DateFilterPicker`/`ReportingDateRangePickerControl`/`FilterBar`/`TopTabs`/`ViewSwitcher`/`CameraModal` → WP07/Phase 4). Cross-checked the real WP01 shell (`index.css` ad-hoc `--ink/--paper/...` tokens; `AppLayout.tsx`), the WP03 render-edge formatters, the `eslint.config.js` boundary rules (no `shared/**` rule yet — leaf precedent from i18n), and the page-budget tool (targets only `App.tsx`/`*Page.tsx`, so primitives are uncounted). **Confirmed spec-derived salvage of presentation primitives + token synthesis, no golden fixtures, no accounting consult.** Six front-loaded non-accounting decisions surfaced for user sign-off: **D-P3-UI-TOKENS** (canonical token file, migrate shell props, no visual change), **D-P3-UI-RESPONSIVE** (drop table breakpoint), **D-P3-UI-AMOUNT** (rewrite money input integer-safe), **D-P3-UI-THEME** (defer theming to Phase 4), **D-P3-UI-PICKERS** (generic date field only; domain pickers → WP06), **D-P3-UI-BOUNDARY** (add shared-leaf ESLint rule). 10 concrete ACs; WP04a/WP04b split seam documented. Implementation blocked on user approval of the decisions.
- **2026-07-14 — User / LL Architect:** **All six decisions and the plan approved on their recommended routes** (D-P3-UI-TOKENS, -RESPONSIVE, -AMOUNT, -THEME, -PICKERS, -BOUNDARY). No overrides. Decisions are now implementation constraints; the plan is unblocked. State stays `planned`, ready for LL Frontend Dev.
- **2026-07-14 — LL Frontend Dev:** Implemented the canonical token layer and migrated the shell aliases; added the desktop-only `DataTable`, `ModalShell`, `FormSection`, `FormField`, `ToggleSwitch`, generic `DateField`, and integer-safe `MoneyInput`; exported the public primitives from the shared leaf and added the approved ESLint boundary. Added focused shared coverage (**9/9**) for table states/actions, modal portal/accessibility/closing, controlled fields, and USD/JPY/BHD/negative/large money parsing. No `app/src/api/**`, backend, OpenAPI, or runtime dependency changes. Full frontend validation: Vitest **37/37**, lint, typecheck, strict page budget, production build, duplicate-key check, and `npm audit --omit=dev` (**0 vulnerabilities**) pass. The build retains the existing large-chunk warning. State → `verify`; next action is LL QA Reviewer acceptance review.
- **2026-07-14 — LL Frontend Dev:** Addressed QA findings: generalized `FormField` with an explicit custom control slot while retaining the input convenience path; added contract tests outside the shared leaf for the canonical token set/import and shell aliases, desktop-only/no-float implementation constraints, and deferred-module/data-path absence. Focused remediation tests **12/12** and full frontend Vitest **40/40** pass. Lint, typecheck, strict page budget, production build, duplicate-key check, `npm audit --omit=dev` (**0 vulnerabilities**), `git diff --check`, and generated/backend scope checks pass. The build retains the existing large-chunk warning. State → `verify`; next action is LL QA Reviewer re-review.

## QA verdict

**PASS — 2026-07-14 — LL QA Reviewer**

1. **FormField control API:** [FormField.tsx](../../../app/src/shared/FormField.tsx) accepts a generic custom `control` element, preserves the input convenience path, and injects the field id, invalid state, and description/error ARIA wiring. Component coverage exercises a custom `<select>`.
2. **Canonical token evidence:** [shared-contract.test.ts](../../../app/tests/shared-contract.test.ts) asserts the documented token set, the single import from [index.css](../../../app/src/index.css), and the migrated shell aliases.
3. **Non-port and source constraints:** The same contract suite pins desktop-only `DataTable`, float-free `MoneyInput`, and absence of deferred responsive, space-preference, theme, domain-picker, and data-fetch machinery. The independent scope scan found no backend, OpenAPI, generated API, or data-access changes.

**Reproduced gates:** Vitest **40/40** across 14 files; lint, strict typecheck, strict page budget, duplicate-key check, production build, and `git diff --check` pass. `npm audit --omit=dev` reports **0 vulnerabilities**. The existing production large-chunk warning remains non-blocking. No financial, authorization, backend, OpenAPI, or secret-handling surface is introduced by this presentation-only WP.

**Verdict:** All planned acceptance criteria and prior QA findings are satisfied. State → **done**. **Next action:** proceed to P3-WP05 planning/implementation.
