# SOURCE — ledger-core golden fixtures provenance

Golden fixtures pinning the Phase-2 ledger-core posting rules. Every `expected`
value in this set was **captured by executing the OLD implementation** (never
hand-authored) via the committed harness
[`tools/fixtures/ledger-core/capture.test.ts`](../../../../tools/fixtures/ledger-core/capture.test.ts).
See [`tools/fixtures/README.md`](../../../../tools/fixtures/README.md) for the
reproducible re-capture procedure.

## Old-repo pin

| | |
|---|---|
| Repo | [`Lokkeccs/Accounting`](https://github.com/Lokkeccs/Accounting) (read-only reference) |
| Commit SHA | `085bedba467e3d46d3889db3bc80ea023e69756e` |
| Local checkout | `C:\Programming\LeafLedger\Accounting` |
| Captured | 2026-07-06 |
| Capture timezone | `TZ=UTC` (stabilizes `setHours(0,0,0,0)` / `23,59,59,999` day boundaries) |
| Case count | 59 (see `manifest.json`) |

## Per-unit source references (at the pinned SHA)

### `src/shared/postingValidity.ts`
| Symbol | Line |
|---|---|
| `PostingValidityError` (error class) | L67 |
| `evaluateAccountReference` | L96 |
| `evaluateTimeboundReference` | L114 |
| `assertPostingAccountsValid` | L134 |
| `assertPostingBusinessPartnersValid` | L163 |
| `assertPostingUsersValid` | L192 |
| `assertPostingProjectsValid` | L221 |
| `CurrencyPolicyError` (error class) | L287 |
| `resolveEffectiveCurrencyPolicy` | L302 |
| `assertPostingCurrencyPolicyValid` | L313 |
| `PeriodClosedError` (error class) | L353 |
| `assertPostingPeriodOpen` | L376 |

### `src/shared/periodUtils.ts`
| Symbol | Line |
|---|---|
| `getPeriodForDate` | L122 |
| `getEffectivePeriodState` | L136 |

### `src/shared/fxPolicy.ts`
| Symbol | Line |
|---|---|
| `MONETARY_GROUPS` | L27 |
| `HISTORICAL_GROUPS` | L39 |
| `CURRENT_VALUE_GROUPS` | L52 |
| `inferBusinessFxPolicy` | L78 |
| `resolveGroupFxPolicy` | L153 |
| `resolveAccountFxPolicy` | L168 |
| `buildTransactionLineFxMetadata` | L190 |

## Seed test files (branch-coverage source)

Old-repo suites whose cases were reproduced and then extended to full branch +
boundary coverage:

- `tests/postingValidity.test.ts`
- `tests/accountCurrencyPolicy.test.ts`
- `tests/periodUtils.test.ts`

## Notable behaviors pinned (asymmetries the port must reproduce)

- **Accounts**: `inactive` is rejected in **both** business and personal
  (checked before the purpose branch); `future`/`expired` window checks are
  **business-only**.
- **Business partners / users** (`evaluateTimeboundReference`): `inactive`
  (`isActive === false`) is rejected in **personal only**; `future`/`expired`
  are **business-only**. A business `isActive:false` reference therefore
  **passes** — see `bp-inactive-business-allowed`, `user-inactive-business-allowed`.
- **Projects**: only `missing | future | expired` (from `startDate`/`endDate`);
  no `inactive`, no purpose gating.
- **Currency policy**: income/expense → `any`; asset/liability/equity → `fixed`
  to account currency; case-insensitive/trimmed compare; missing accounts and
  empty currencies are skipped; multiple violations are collected.
- **Period**: `closed`/`locked` → `PeriodClosedError`; `no-period-defined` →
  posting **allowed** (backward compatibility).
- **FX metadata**: account-level overrides beat group inference; personal is
  always historical/no-revalue; `fxRateDate` is midnight-normalized.

## Out of scope (deferred — do not add here)

Balance/tolerance math (UI float, → DB trigger + property suite in P2-WP08),
`formatPostingValidityError` message strings (UX/i18n), period *generation*
(`generateAccountingPeriods`), VAT / FX-revaluation / period-close engines
(Phase-4/M2). See the P2-WP01 plan Non-goals.
