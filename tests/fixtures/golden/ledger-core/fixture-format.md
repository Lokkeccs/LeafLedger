# Fixture format — ledger-core golden fixtures

Language-neutral JSON, loadable by both the new C# domain tests and the thin TS
pre-validation mirror without bespoke per-unit parsing. Each case file is:

```jsonc
{
  "input":    { /* unit-specific, plain data — see per-unit shapes below */ },
  "expected": { "ok": true }            // the old function returned without throwing
              | { "error": { "type": …, … } }  // the old function threw a typed error
              | { "value": … }          // the old function returned a value (pure query)
}
```

Exactly one of `ok` / `error` / `value` is present:

- **`{ "ok": true }`** — for the `assert…` guards that return `void` on success.
- **`{ "error": { "type": …, … } }`** — for the `assert…` guards that throw. See
  *Error shapes* below.
- **`{ "value": … }`** — for the pure query/derivation units
  (`getEffectivePeriodState`, `resolveGroupFxPolicy`, `resolveAccountFxPolicy`,
  `buildTransactionLineFxMetadata`).

## Conventions

- **Dates**: ISO `yyyy-MM-dd` only — no time-of-day, no timezone. Consumers
  interpret each as that calendar day. The old code normalizes window bounds to
  start-of-day (`00:00:00.000`) / end-of-day (`23:59:59.999`); period ranges are
  inclusive whole days `[startDate 00:00 … endDate 23:59:59.999]`.
- **Optional fields** (`source`, `validFrom`, `validTo`, `fxTreatment`, …) are
  **omitted** when absent (never `null`), mirroring `undefined` in the old code.
- **Amounts**: none in this set — all units are master-data + date/policy logic.
- Files are deterministic: 2-space indent, trailing newline, stable key order.

## `manifest.json`

Index of every case, 1:1 with the case files:

```jsonc
{
  "source": { "repo": "Lokkeccs/Accounting", "sha": "<40-hex>" },
  "count": 59,
  "cases": [ { "id": "acc-missing-business", "unit": "assertPostingAccountsValid",
               "description": "…", "file": "posting-accounts/acc-missing-business.json" }, … ]
}
```

## Per-unit `input` shapes

| Folder | `unit` | `input` |
|---|---|---|
| `posting-accounts` | `assertPostingAccountsValid` | `{ purpose: "business"｜"personal", accounts: [{ id, isActive, validFrom?, validTo? }], references: [{ accountId, txDate, source? }] }` |
| `posting-business-partners` | `assertPostingBusinessPartnersValid` | `{ purpose, businessPartners: [{ id, isActive?, validFrom?, validTo? }], references: [{ businessPartnerId, txDate, source? }] }` |
| `posting-users` | `assertPostingUsersValid` | `{ purpose, users: [{ id, isActive?, validFrom?, validTo? }], references: [{ userId, txDate, source? }] }` |
| `posting-projects` | `assertPostingProjectsValid` | `{ projects: [{ id, startDate?, endDate? }], references: [{ projectId, txDate, source? }] }` |
| `currency-policy` | `assertPostingCurrencyPolicyValid` | `{ accounts: [{ id, currency, type }], references: [{ accountId, txCurrency, source? }] }` |
| `period-state` | `getEffectivePeriodState` | `{ txDate, periods: [{ name, startDate, endDate, state, fiscalYear?, granularity? }] }` → `value` |
| `period-state` | `assertPostingPeriodOpen` | `{ txDate, periods: [ … ] }` → `ok` / `error` |
| `fx-metadata` | `buildTransactionLineFxMetadata` | `{ purpose, account: { currency, type, group, fxTreatment?, fxRateTimingDefault?, closingRevalue?, vatFxMethodOverride? }, txDate }` → `value` |
| `fx-metadata` | `resolveAccountFxPolicy` | `{ purpose, account: { type, group, …overrides }, groupPolicy? }` → `value` |
| `fx-metadata` | `resolveGroupFxPolicy` | `{ purpose, type, group, groupPolicy? }` → `value` |

`type` ∈ `asset｜liability｜equity｜income｜expense`. `state` ∈ `open｜closed｜locked`.

## `value` shapes

- **`getEffectivePeriodState`** → a string: `"open"｜"closed"｜"locked"｜"no-period-defined"`.
- **`buildTransactionLineFxMetadata`** →
  `{ fxRateDate (yyyy-MM-dd), fxRateTiming, fxTreatmentApplied, fxClosingRevalueApplied (bool), fxVatMethodApplied, fxCurrency }`.
- **`resolveGroupFxPolicy` / `resolveAccountFxPolicy`** →
  `{ fxTreatment, fxRateTimingDefault, closingRevalue (bool), vatFxMethodOverride }`.
  - `fxTreatment` ∈ `monetary｜historical｜current-value`
  - `fxRateTiming(Default)` ∈ `transaction-date｜settlement-date｜valuation-date`
  - `vatFxMethod(Override/Applied)` ∈ `space-default｜daily｜monthly-average`

## Error shapes

```jsonc
// PostingValidityError — accounts / business partners / users / projects
{ "type": "PostingValidityError",
  "issues": [ { "entityType": "account"｜"businessPartner"｜"user"｜"project",
                "entityId": <int>,
                "reason": "missing"｜"inactive"｜"future"｜"expired",
                "txDate": "yyyy-MM-dd", "source"?: <string> } ] }

// CurrencyPolicyError — currency policy
{ "type": "CurrencyPolicyError",
  "issues": [ { "accountId": <int>, "accountCurrency": <UPPER>, "txCurrency": <UPPER>,
                "reason": "currency-not-allowed", "source"?: <string> } ] }

// PeriodClosedError — period open guard
{ "type": "PeriodClosedError",
  "periodName": <string>, "state": "closed"｜"locked", "txDate": "yyyy-MM-dd" }
```

`issues[]` preserves reference order and collects every violation in one throw.
