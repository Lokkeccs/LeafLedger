# LeafLedger Rebuild Analysis — Part 6: Feature Roadmap (MVP → Later)

> Companion to [04-implementation-plan.md](./04-implementation-plan.md). Scopes the greenfield rebuild into delivery tiers. The feature inventory is derived from the audited current app (17 feature folders + backend capabilities, Part 1 §2/§7).
>
> **Scoping principle:** adoption is per user/space. **No data is migrated** — existing users start fresh spaces in the new app (assisted by CSV export from old → import into new, plus an opening-balance path). A user can switch as soon as every feature they *actually use* is covered: simple personal/business users switch at M1; subledger-heavy users when M2 lands.
>
> **Client strategy (2026-07-04):** the full product is **desktop-first** (web + Microsoft Store MSIX of the same build); phones get a lightweight **companion app** (dashboards + document capture) in M3 — phone feature-parity is a non-goal.

## Tier overview

| Tier | Name | Gate | Maps to plan phases |
|---|---|---|---|
| M0 | Platform foundation | Nothing user-visible ships without it | P1–P2 |
| M1 | **MVP — core accounting** | Launch: new signups + first switchers | P2–P4 (first half) |
| M2 | Fast-follow — full parity | Every current user's feature set covered; old stack goes read-only | P4 (second half)–P5 |
| M3 | Post-launch — new capabilities | Product growth, not launch-blocking | after P7 |
| — | Dropped | Obsolete by architecture | — |

## M0 — Platform foundation (not user-visible)

- Monorepo + CI (all gates blocking), docker-compose local dev
- Entra auth (MSAL, sessionStorage), identity links
- Spaces: create business/personal, solo/shared; memberships, typed roles, invitations; feature-policy matrix (business/personal audience)
- **Server-side license enforcement** (plans free/pro/bundle, device binding, entitlement middleware)
- Postgres schema core + RLS + audit triggers + balance constraint
- OpenAPI → generated TS client pipeline
- **Golden fixtures** (built early — they are the porting oracle, not an afterthought)
- Observability baseline (OTel → App Insights, `/health`, `/integrity`)

## M1 — MVP: core accounting (launch scope)

Everything a simple business or personal space needs day-to-day:

| Area | Included in MVP | Deliberately thinner than today |
|---|---|---|
| Chart of accounts | Accounts CRUD, built-in + custom groups, code ranges, active/inactive, account ownership, account import/export (CSV/XLSX incl. owner-by-email) | FX policy fields present but advanced overrides UI minimal |
| Journal | Manual entry (single flow), balanced-entry UX with server validation, **reversal-based corrections**, references/memos, business partner/project links, attachments (SAS upload/download) | No pending-entry auto-promotion logic — pending = explicit draft status |
| Multi-currency | Multi-currency posting with base amounts + stored rates, daily FX rate service, rate override at entry | Period-close FX revaluation moves to M2 |
| VAT | VAT codes, per-line VAT, basic VAT report (Swiss net/effective) | VAT period workflows polish in M2 |
| Periods | Accounting periods, open/close/lock enforcement at posting | Full period-close engine (equity rollover) in M2 |
| Attribution | Per-line ownership % splits (sum = 100), derivation from account ownership — core for shared personal spaces | — |
| Imports | CSV/XLSX import: column mapping, bank/full/split modes, **server-side staging**, row editing, automation rules (description-assign, transfer-match), booking via posting API, import batch history | Settlement-matching + investment-lot columns in M2 |
| Master data | Users, business partners (+accounts/groups), projects, currencies — CRUD + import/export | — |
| Reports & dashboard | Trial balance, balance sheet, P&L, account detail/drill, overview dashboard (server-computed) | Cash-flow statement, pivots, drill-everything in M2 |
| Shell | i18n (all 5 locales from day one — corpus ports wholesale), theming, date/number format prefs, installable PWA shell + **Microsoft Store MSIX package**, help-site link. **Desktop-first**: min width ~tablet landscape; phone parity is explicitly NOT an M1 goal (companion app covers phones in M3) | Onboarding = functional space-creation wizard; guided "Get Started" checklists in M2 |
| Admin | Space settings, member management, license activation | Per-space purge (admin API) — M0 backend, UI in M2 |

**MVP acceptance:** golden-master parity on the reference ledgers + invariant suite green + a Playwright journey for every M1 flow — plus a validated self-migration path (old-app export → new-app import round-trip for accounts, journal, master data).

## M2 — Fast-follow: full parity (unblocks all remaining spaces)

- **Real estate subledger** (properties, units, costs, mortgages, rental income, maintenance/cap-improvements, equity entries, journal generators)
- **Investments subledger** (positions, lots buy/sell/dividend, scheduled acquisitions, manual + fetched prices, GL configs, sell-lot posting with costs + attribution)
- Period-close engine: closing/equity rollover, **FX revaluation** (OR-conservative investment policy carried over)
- Budgets (plans, targets, vs-actual reporting)
- Advanced reporting: cash-flow statement (activity inference from groups), pivot engine, dashboard drill parity
- Import pipeline completions: settlement matching, amount-transfer matching, investment-lot columns, bulk edit parity
- Notifications (in-app), guided onboarding/Get-Started flows
- Equity/opening-balance tooling
- Admin: space purge UI, audit-log viewer (new — the audit trail exists, expose it)

**M2 exit = every current user's feature set is covered → old stack goes read-only → sunset → decommission (Phase 7).**

## M3 — Post-launch: new capabilities (product growth)

Priority-ordered proposal:

1. **Phone companion app** (small React PWA from `app/companion/`): dashboards (read-only), **document/QR-bill capture** → upload → Document Intelligence → prefilled entry proposal appears in the full app; notifications. This is where the Swiss QR-Rechnung flagship lands — capture on the phone, book on the desktop. (Phase 5 builds the Documents module; companion UX matures here. Capacitor wrapper only if store listings are wanted.)
2. **Bank statement automation**: camt.053 import, then bank-API connectivity (EBICS/openbanking) — builds on the Imports module.
3. Web push notifications (re-introduced server-driven — reminders, invitations, period-close nudges)
4. Recurring/template entries & scheduled posting
5. Audit & compliance pack: exportable audit trail, retention policies, accountant read-only access role
6. KNX/webhook integrations (Integrations module stub from Phase 5)
7. Offline **server-drafts** — only if post-launch usage data proves demand (Part 4, risk #4)
8. Multi-space consolidation reporting / e-invoicing — backlog, revisit with users

## Dropped (obsolete by architecture — do not port)

- Sync feature UI: conflict modal, sync progress, per-subledger sync diagnostics panels, cache-clear recovery flows
- Offline mutation queue, changeLog/entitySeqs machinery, gap-fill, monthlySummaries self-heal (reports are server-computed)
- Client-side license gating, tesseract.js OCR, OPFS storage, `Accounting_docs/` parallel app
- Legacy delta-era admin tooling (`temp-*.py` capabilities become audited admin APIs or die)

## Switch-over readiness (operational view)

| User profile | Needs | Can switch after |
|---|---|---|
| Personal solo, journal + imports only | M1 | M1 launch |
| Business solo/shared, no subledgers | M1 (+ VAT) | M1 launch |
| Personal shared with attribution | M1 | M1 launch |
| Anyone using real estate / investments / budgets / period-close | M2 | M2 completion |

During Phase 0, run a small usage probe against the old backend (which tables have rows per space) to know how many users are M1-ready vs M2-blocked — it sizes the launch waves and prioritizes M2 ordering.
