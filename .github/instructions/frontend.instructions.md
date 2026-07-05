---
applyTo: "app/**"
---
# Frontend rules (any agent touching `app/**`)

These attach automatically to every agent editing frontend code. Rules are enforced by ESLint + the page-budget gate + vitest in CI — not the honor system.

## Layering & boundaries (Part 3 §7 — enforced by ESLint)
- Flow is **features → application → api**. Feature pages/components import only from the application layer and local files — never from `src/api` or the app shell.
- The **application layer** (TanStack Query hooks, view-model logic) must not import feature UI or the app shell.
- Data access is **only** through the generated client in `src/api/` (P1-WP04). Never call `fetch`/`window.fetch` directly; never hand-edit generated files — regenerate instead. `src/api/**` is lint-ignored and import-restricted.

## Page budget (enforced by `tools/check-page-budget.cjs`, blocking)
- Budget: **450 lines / 30 imports / 20 state hooks** per page (error at 550/40/28). Decompose *before* you exceed it. `JournalEntryPage`-scale files are structurally forbidden.

## Design system (desktop-first)
- The full product is **desktop-first** (min width ~tablet landscape). Mobile responsiveness is **not** a requirement for the full app — do not port the old responsive-table machinery. Only the future companion client is phone-optimized.
- Favor **multi-pane / dense layouts and keyboard shortcuts**. Prefer keyboard-first interactions for data-heavy flows.
- **Reuse shared primitives before writing new UI** (DataTable, pickers, modals, form primitives). New one-off components are a smell — check the shared library first.
- Use **design tokens** (colors, spacing, radius, shadows via CSS custom properties) — never hard-code raw hex/px where a token exists. Tokens live in the shared design module (final path settles at the P3 re-platform; today: shared styles/tokens under `app/src`).

## Conventions
- **i18n:** every new user-facing string is a translation key added to **all locale files** in the same change — never hard-code copy.
- **IDs** from the API are prefixed ULIDs (`je_…`, `acc_…`); treat them as opaque strings, never parse or assume numeric.
- **Money** arrives as integer minor units + ISO currency; format only at the render edge. Never do float math on amounts.
- Client-side validation is **UX only**; the server is authoritative. Mirror server rules only where the plan says so, pinned by shared golden fixtures.

## Done means
`npm run lint`, `npm run typecheck`, `npm test`, and `npm run check:page-budget` green; vitest coverage for view-model logic (and a Playwright journey where the plan requires). Set WP state "verify". No commits — LL Git handles that.
