/**
 * P2-WP01 — Golden fixture capture harness (ledger-core posting rules).
 *
 * PURPOSE
 *   Executes the OLD (read-only) Accounting implementation to capture exact
 *   input -> outcome artifacts for the Phase-2 posting rules, so the new C#
 *   domain and the thin TS pre-validation mirror can be graded against real
 *   old-code behavior "to the branch". Expected values are NEVER hand-authored:
 *   every `expected` block below is produced by invoking the old functions.
 *
 * HOW TO RUN (see tools/fixtures/README.md for the full procedure)
 *   This file is committed in the NEW repo but must run inside the OLD repo's
 *   own toolchain (vitest, TS config, Node). It is copied into the old repo's
 *   `tests/` directory, executed, then the temporary copy is deleted so the old
 *   repo stays pristine:
 *
 *     $env:TZ = 'UTC'                                   # deterministic date math
 *     Copy-Item <newRepo>/tools/fixtures/ledger-core/capture.test.ts `
 *               <oldRepo>/tests/_goldenCapture.test.ts
 *     cd <oldRepo>; npx vitest run tests/_goldenCapture.test.ts
 *     Remove-Item <oldRepo>/tests/_goldenCapture.test.ts
 *
 *   Output dir override: set LL_FIXTURE_OUT to an absolute path. Default assumes
 *   the new repo is a sibling folder named "LeafLedger" next to the old repo.
 *
 * DETERMINISM
 *   - Run under TZ=UTC so `setHours(0,0,0,0)` / `23,59,59,999` boundaries and
 *     `new Date('yyyy-MM-dd')` (UTC midnight) coincide -> stable future/expired.
 *   - No wall-clock values are written into artifacts (SHA read from git only).
 *   - JSON is stringified with 2-space indent + trailing newline, stable key
 *     order -> re-running against the pinned SHA yields byte-identical files.
 */

import { execSync } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { it } from 'vitest';

import {
  assertPostingAccountsValid,
  assertPostingBusinessPartnersValid,
  assertPostingUsersValid,
  assertPostingProjectsValid,
  assertPostingCurrencyPolicyValid,
  assertPostingPeriodOpen,
  PostingValidityError,
  CurrencyPolicyError,
  PeriodClosedError,
} from '../src/shared/postingValidity';
import { getEffectivePeriodState } from '../src/shared/periodUtils';
import {
  buildTransactionLineFxMetadata,
  resolveAccountFxPolicy,
  resolveGroupFxPolicy,
} from '../src/shared/fxPolicy';

// ─── date helpers (mirror the old code's local-time normalization) ────────────
function atStartOfDay(value: Date): Date {
  const next = new Date(value);
  next.setHours(0, 0, 0, 0);
  return next;
}
function atEndOfDay(value: Date): Date {
  const next = new Date(value);
  next.setHours(23, 59, 59, 999);
  return next;
}
function isoDate(value: Date): string {
  const y = value.getFullYear();
  const m = String(value.getMonth() + 1).padStart(2, '0');
  const d = String(value.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}
function d(iso?: string | null): Date | null {
  return iso ? new Date(iso) : null;
}

// ─── input -> old-code object builders ────────────────────────────────────────
type Any = Record<string, unknown>;

function toAccount(a: Any) {
  return {
    id: a.id as number,
    isActive: a.isActive as boolean,
    validFrom: d(a.validFrom as string | null | undefined),
    validTo: d(a.validTo as string | null | undefined),
  };
}
function toTimebound(a: Any) {
  return {
    id: a.id as number,
    isActive: a.isActive as boolean | undefined,
    validFrom: d(a.validFrom as string | null | undefined),
    validTo: d(a.validTo as string | null | undefined),
  };
}
function toProject(p: Any) {
  return {
    id: p.id as number,
    startDate: d(p.startDate as string | null | undefined),
    endDate: d(p.endDate as string | null | undefined),
  };
}
function toPeriod(p: Any) {
  const start = new Date(p.startDate as string);
  return {
    name: p.name as string,
    startDate: atStartOfDay(start),
    endDate: atEndOfDay(new Date(p.endDate as string)),
    fiscalYear: (p.fiscalYear as number | undefined) ?? start.getFullYear(),
    granularity: (p.granularity as 'monthly' | 'quarterly' | undefined) ?? 'monthly',
    state: p.state as 'open' | 'closed' | 'locked',
    createdAt: new Date(0),
    updatedAt: new Date(0),
  };
}

// ─── error serialization (pins the exact error shape) ─────────────────────────
function serializeError(e: unknown): Any {
  if (e instanceof PostingValidityError) {
    return {
      type: 'PostingValidityError',
      issues: e.issues.map((i) => stripUndefined({
        entityType: i.entityType,
        entityId: i.entityId,
        reason: i.reason,
        txDate: i.txDate,
        source: i.source,
      })),
    };
  }
  if (e instanceof CurrencyPolicyError) {
    return {
      type: 'CurrencyPolicyError',
      issues: e.issues.map((i) => stripUndefined({
        accountId: i.accountId,
        accountCurrency: i.accountCurrency,
        txCurrency: i.txCurrency,
        reason: i.reason,
        source: i.source,
      })),
    };
  }
  if (e instanceof PeriodClosedError) {
    return {
      type: 'PeriodClosedError',
      periodName: e.periodName,
      state: e.state,
      txDate: e.txDate,
    };
  }
  throw e; // unexpected throw -> fail the capture loudly, never silently pin it
}
function stripUndefined(o: Any): Any {
  const out: Any = {};
  for (const [k, v] of Object.entries(o)) if (v !== undefined) out[k] = v;
  return out;
}

// ─── case definitions ─────────────────────────────────────────────────────────
interface Case {
  id: string;
  unit: string;
  folder: string;
  description: string;
  input: Any;
}

const cases: Case[] = [
  // ── posting-accounts (assertPostingAccountsValid) ──────────────────────────
  {
    id: 'acc-missing-business', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'unknown account id -> missing (business)',
    input: { purpose: 'business', accounts: [], references: [{ accountId: 99, txDate: '2026-05-15', source: 'row-1' }] },
  },
  {
    id: 'acc-inactive-business', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'inactive account rejected in business (inactive checked before purpose)',
    input: { purpose: 'business', accounts: [{ id: 1, isActive: false }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-inactive-personal', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'inactive account rejected in personal too (window ignored, inactive still fails)',
    input: { purpose: 'personal', accounts: [{ id: 1, isActive: false }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-future-business', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'posting date before validFrom -> future (business)',
    input: { purpose: 'business', accounts: [{ id: 1, isActive: true, validFrom: '2026-06-01' }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-expired-business', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'posting date after validTo -> expired (business)',
    input: { purpose: 'business', accounts: [{ id: 1, isActive: true, validTo: '2026-04-30' }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-personal-ignores-window', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'personal ignores validFrom/validTo window -> allowed',
    input: { purpose: 'personal', accounts: [{ id: 1, isActive: true, validFrom: '2026-06-01', validTo: '2026-06-30' }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-valid-in-window-business', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'active and inside business window -> allowed',
    input: { purpose: 'business', accounts: [{ id: 1, isActive: true, validFrom: '2026-05-01', validTo: '2026-05-31' }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-boundary-on-validfrom', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'posting date exactly on validFrom -> allowed (start-of-day boundary)',
    input: { purpose: 'business', accounts: [{ id: 1, isActive: true, validFrom: '2026-05-15' }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-boundary-on-validto', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'posting date exactly on validTo -> allowed (end-of-day boundary)',
    input: { purpose: 'business', accounts: [{ id: 1, isActive: true, validTo: '2026-05-15' }], references: [{ accountId: 1, txDate: '2026-05-15' }] },
  },
  {
    id: 'acc-multi-issue', unit: 'assertPostingAccountsValid', folder: 'posting-accounts',
    description: 'multiple invalid references collected into issues[]',
    input: {
      purpose: 'business',
      accounts: [{ id: 1, isActive: true, validFrom: '2026-06-01' }, { id: 2, isActive: false }],
      references: [{ accountId: 1, txDate: '2026-05-15', source: 'row-a' }, { accountId: 2, txDate: '2026-05-15', source: 'row-b' }, { accountId: 3, txDate: '2026-05-15', source: 'row-c' }],
    },
  },

  // ── posting-business-partners (assertPostingBusinessPartnersValid) ─────────
  {
    id: 'bp-missing', unit: 'assertPostingBusinessPartnersValid', folder: 'posting-business-partners',
    description: 'unknown business partner -> missing',
    input: { purpose: 'business', businessPartners: [], references: [{ businessPartnerId: 5, txDate: '2026-05-15' }] },
  },
  {
    id: 'bp-inactive-personal', unit: 'assertPostingBusinessPartnersValid', folder: 'posting-business-partners',
    description: 'isActive:false rejected in personal (inactive is personal-only for timebound refs)',
    input: { purpose: 'personal', businessPartners: [{ id: 5, isActive: false }], references: [{ businessPartnerId: 5, txDate: '2026-05-15' }] },
  },
  {
    id: 'bp-inactive-business-allowed', unit: 'assertPostingBusinessPartnersValid', folder: 'posting-business-partners',
    description: 'isActive:false is IGNORED in business (asymmetry vs personal) -> allowed',
    input: { purpose: 'business', businessPartners: [{ id: 5, isActive: false }], references: [{ businessPartnerId: 5, txDate: '2026-05-15' }] },
  },
  {
    id: 'bp-future-business', unit: 'assertPostingBusinessPartnersValid', folder: 'posting-business-partners',
    description: 'window future -> rejected in business',
    input: { purpose: 'business', businessPartners: [{ id: 5, validFrom: '2026-06-01', validTo: '2026-06-30' }], references: [{ businessPartnerId: 5, txDate: '2026-05-15' }] },
  },
  {
    id: 'bp-expired-business', unit: 'assertPostingBusinessPartnersValid', folder: 'posting-business-partners',
    description: 'window expired -> rejected in business',
    input: { purpose: 'business', businessPartners: [{ id: 5, validTo: '2026-05-01' }], references: [{ businessPartnerId: 5, txDate: '2026-05-15' }] },
  },
  {
    id: 'bp-personal-ignores-window', unit: 'assertPostingBusinessPartnersValid', folder: 'posting-business-partners',
    description: 'personal ignores future/expired windows -> allowed',
    input: { purpose: 'personal', businessPartners: [{ id: 5, validFrom: '2026-06-01', validTo: '2026-06-30' }], references: [{ businessPartnerId: 5, txDate: '2026-05-15' }] },
  },
  {
    id: 'bp-valid-business', unit: 'assertPostingBusinessPartnersValid', folder: 'posting-business-partners',
    description: 'active and inside window -> allowed',
    input: { purpose: 'business', businessPartners: [{ id: 5, isActive: true, validFrom: '2026-05-01', validTo: '2026-05-31' }], references: [{ businessPartnerId: 5, txDate: '2026-05-15' }] },
  },

  // ── posting-users (assertPostingUsersValid) ────────────────────────────────
  {
    id: 'user-missing', unit: 'assertPostingUsersValid', folder: 'posting-users',
    description: 'unknown user -> missing',
    input: { purpose: 'business', users: [], references: [{ userId: 7, txDate: '2026-05-15' }] },
  },
  {
    id: 'user-inactive-personal', unit: 'assertPostingUsersValid', folder: 'posting-users',
    description: 'isActive:false rejected in personal',
    input: { purpose: 'personal', users: [{ id: 7, isActive: false }], references: [{ userId: 7, txDate: '2026-05-15' }] },
  },
  {
    id: 'user-inactive-business-allowed', unit: 'assertPostingUsersValid', folder: 'posting-users',
    description: 'isActive:false ignored in business -> allowed (asymmetry)',
    input: { purpose: 'business', users: [{ id: 7, isActive: false }], references: [{ userId: 7, txDate: '2026-05-15' }] },
  },
  {
    id: 'user-future-business', unit: 'assertPostingUsersValid', folder: 'posting-users',
    description: 'window future -> rejected in business',
    input: { purpose: 'business', users: [{ id: 7, validFrom: '2026-06-01' }], references: [{ userId: 7, txDate: '2026-05-15' }] },
  },
  {
    id: 'user-expired-business', unit: 'assertPostingUsersValid', folder: 'posting-users',
    description: 'window expired -> rejected in business',
    input: { purpose: 'business', users: [{ id: 7, validTo: '2026-05-01' }], references: [{ userId: 7, txDate: '2026-05-15' }] },
  },
  {
    id: 'user-personal-ignores-window', unit: 'assertPostingUsersValid', folder: 'posting-users',
    description: 'personal ignores window -> allowed',
    input: { purpose: 'personal', users: [{ id: 7, validTo: '2026-05-01' }], references: [{ userId: 7, txDate: '2026-05-15' }] },
  },
  {
    id: 'user-valid-business', unit: 'assertPostingUsersValid', folder: 'posting-users',
    description: 'active and inside window -> allowed',
    input: { purpose: 'business', users: [{ id: 7, isActive: true, validFrom: '2026-05-01', validTo: '2026-05-31' }], references: [{ userId: 7, txDate: '2026-05-15' }] },
  },

  // ── posting-projects (assertPostingProjectsValid) ──────────────────────────
  {
    id: 'project-missing', unit: 'assertPostingProjectsValid', folder: 'posting-projects',
    description: 'unknown project -> missing',
    input: { projects: [], references: [{ projectId: 10, txDate: '2026-05-15', source: 'transaction:projectId' }] },
  },
  {
    id: 'project-future', unit: 'assertPostingProjectsValid', folder: 'posting-projects',
    description: 'posting date before startDate -> future',
    input: { projects: [{ id: 10, startDate: '2026-06-01', endDate: '2026-06-30' }], references: [{ projectId: 10, txDate: '2026-05-15', source: 'transaction:projectId' }] },
  },
  {
    id: 'project-expired', unit: 'assertPostingProjectsValid', folder: 'posting-projects',
    description: 'posting date after endDate -> expired',
    input: { projects: [{ id: 10, startDate: '2026-05-01', endDate: '2026-05-31' }], references: [{ projectId: 10, txDate: '2026-06-15', source: 'transaction:projectId' }] },
  },
  {
    id: 'project-valid-in-window', unit: 'assertPostingProjectsValid', folder: 'posting-projects',
    description: 'posting date inside window -> allowed',
    input: { projects: [{ id: 10, startDate: '2026-05-01', endDate: '2026-05-31' }], references: [{ projectId: 10, txDate: '2026-05-15', source: 'transaction:projectId' }] },
  },
  {
    id: 'project-no-window', unit: 'assertPostingProjectsValid', folder: 'posting-projects',
    description: 'no start/end date -> allowed',
    input: { projects: [{ id: 10 }], references: [{ projectId: 10, txDate: '2026-05-15', source: 'transaction:projectId' }] },
  },

  // ── currency-policy (assertPostingCurrencyPolicyValid) ─────────────────────
  {
    id: 'cp-income-any', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'P&L income account accepts any tx currency -> allowed',
    input: { accounts: [{ id: 1, currency: 'CHF', type: 'income' }], references: [{ accountId: 1, txCurrency: 'USD' }] },
  },
  {
    id: 'cp-expense-any', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'P&L expense account accepts any tx currency -> allowed',
    input: { accounts: [{ id: 2, currency: 'CHF', type: 'expense' }], references: [{ accountId: 2, txCurrency: 'JPY' }] },
  },
  {
    id: 'cp-asset-match', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'balance-sheet asset, tx currency matches account currency -> allowed',
    input: { accounts: [{ id: 1, currency: 'CHF', type: 'asset' }], references: [{ accountId: 1, txCurrency: 'CHF' }] },
  },
  {
    id: 'cp-asset-mismatch', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'balance-sheet asset, tx currency differs -> currency-not-allowed',
    input: { accounts: [{ id: 1, currency: 'CHF', type: 'asset' }], references: [{ accountId: 1, txCurrency: 'USD', source: 'line-1' }] },
  },
  {
    id: 'cp-liability-mismatch', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'balance-sheet liability mismatch -> currency-not-allowed',
    input: { accounts: [{ id: 2, currency: 'EUR', type: 'liability' }], references: [{ accountId: 2, txCurrency: 'CHF' }] },
  },
  {
    id: 'cp-equity-mismatch', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'balance-sheet equity mismatch -> currency-not-allowed',
    input: { accounts: [{ id: 3, currency: 'CHF', type: 'equity' }], references: [{ accountId: 3, txCurrency: 'USD' }] },
  },
  {
    id: 'cp-case-insensitive', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'currency compare is case-insensitive/trimmed -> allowed',
    input: { accounts: [{ id: 1, currency: 'chf', type: 'asset' }], references: [{ accountId: 1, txCurrency: 'CHF' }] },
  },
  {
    id: 'cp-missing-account-skips', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'missing account skipped here (caught by accounts check) -> allowed',
    input: { accounts: [], references: [{ accountId: 99, txCurrency: 'USD' }] },
  },
  {
    id: 'cp-empty-txcurrency-skips', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'empty tx currency skipped -> allowed',
    input: { accounts: [{ id: 1, currency: 'CHF', type: 'asset' }], references: [{ accountId: 1, txCurrency: '' }] },
  },
  {
    id: 'cp-empty-accountcurrency-skips', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'empty account currency skipped -> allowed',
    input: { accounts: [{ id: 1, currency: '', type: 'asset' }], references: [{ accountId: 1, txCurrency: 'USD' }] },
  },
  {
    id: 'cp-multi-issue', unit: 'assertPostingCurrencyPolicyValid', folder: 'currency-policy',
    description: 'multiple violations collected into issues[]',
    input: {
      accounts: [{ id: 1, currency: 'CHF', type: 'asset' }, { id: 2, currency: 'EUR', type: 'liability' }],
      references: [{ accountId: 1, txCurrency: 'USD' }, { accountId: 2, txCurrency: 'CHF' }],
    },
  },

  // ── period-state (getEffectivePeriodState) — value units ───────────────────
  {
    id: 'ps-state-open', unit: 'getEffectivePeriodState', folder: 'period-state',
    description: 'date within an open period -> "open"',
    input: { txDate: '2026-01-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'open' }] },
  },
  {
    id: 'ps-state-closed', unit: 'getEffectivePeriodState', folder: 'period-state',
    description: 'date within a closed period -> "closed"',
    input: { txDate: '2026-01-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'closed' }] },
  },
  {
    id: 'ps-state-locked', unit: 'getEffectivePeriodState', folder: 'period-state',
    description: 'date within a locked period -> "locked"',
    input: { txDate: '2026-01-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'locked' }] },
  },
  {
    id: 'ps-state-no-period', unit: 'getEffectivePeriodState', folder: 'period-state',
    description: 'date with no covering period -> "no-period-defined"',
    input: { txDate: '2026-05-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'open' }] },
  },

  // ── period-state (assertPostingPeriodOpen) — ok/error units ────────────────
  {
    id: 'ps-open-allowed', unit: 'assertPostingPeriodOpen', folder: 'period-state',
    description: 'open period -> posting allowed',
    input: { txDate: '2026-01-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'open' }] },
  },
  {
    id: 'ps-closed-rejected', unit: 'assertPostingPeriodOpen', folder: 'period-state',
    description: 'closed period -> PeriodClosedError',
    input: { txDate: '2026-01-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'closed' }] },
  },
  {
    id: 'ps-locked-rejected', unit: 'assertPostingPeriodOpen', folder: 'period-state',
    description: 'locked period -> PeriodClosedError',
    input: { txDate: '2026-01-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'locked' }] },
  },
  {
    id: 'ps-no-period-allowed', unit: 'assertPostingPeriodOpen', folder: 'period-state',
    description: 'no covering period -> posting allowed (backward compat)',
    input: { txDate: '2026-05-15', periods: [{ name: 'Jan 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'open' }] },
  },

  // ── fx-metadata (buildTransactionLineFxMetadata) — value units ─────────────
  {
    id: 'fx-monetary-group', unit: 'buildTransactionLineFxMetadata', folder: 'fx-metadata',
    description: 'business asset in a MONETARY group (bank accounts) -> monetary/transaction-date/revalue',
    input: { purpose: 'business', account: { currency: 'USD', type: 'asset', group: 'bank accounts' }, txDate: '2026-05-15' },
  },
  {
    id: 'fx-historical-group', unit: 'buildTransactionLineFxMetadata', folder: 'fx-metadata',
    description: 'business asset in a HISTORICAL group (real estate) -> historical/no-revalue',
    input: { purpose: 'business', account: { currency: 'EUR', type: 'asset', group: 'real estate' }, txDate: '2026-05-15' },
  },
  {
    id: 'fx-default-asset-group', unit: 'buildTransactionLineFxMetadata', folder: 'fx-metadata',
    description: 'business asset in an unknown group -> default historical/no-revalue',
    input: { purpose: 'business', account: { currency: 'USD', type: 'asset', group: 'miscellaneous' }, txDate: '2026-05-15' },
  },
  {
    id: 'fx-default-liability-group', unit: 'buildTransactionLineFxMetadata', folder: 'fx-metadata',
    description: 'business liability in an unknown group -> monetary/revalue (liability fallback)',
    input: { purpose: 'business', account: { currency: 'USD', type: 'liability', group: 'miscellaneous' }, txDate: '2026-05-15' },
  },
  {
    id: 'fx-account-override-wins', unit: 'buildTransactionLineFxMetadata', folder: 'fx-metadata',
    description: 'account-level fxTreatment override wins over group inference',
    input: { purpose: 'business', account: { currency: 'USD', type: 'asset', group: 'bank accounts', fxTreatment: 'current-value', closingRevalue: false }, txDate: '2026-05-15' },
  },
  {
    id: 'fx-personal-historical', unit: 'buildTransactionLineFxMetadata', folder: 'fx-metadata',
    description: 'personal purpose -> historical/no-revalue regardless of group',
    input: { purpose: 'personal', account: { currency: 'USD', type: 'asset', group: 'bank accounts' }, txDate: '2026-05-15' },
  },
  {
    id: 'fx-rate-date-normalized', unit: 'buildTransactionLineFxMetadata', folder: 'fx-metadata',
    description: 'fxRateDate is midnight-normalized to the transaction date',
    input: { purpose: 'business', account: { currency: 'GBP', type: 'asset', group: 'bank accounts' }, txDate: '2026-05-15' },
  },

  // ── fx-metadata (resolveGroupFxPolicy / resolveAccountFxPolicy) ────────────
  {
    id: 'fx-group-policy-fallback', unit: 'resolveGroupFxPolicy', folder: 'fx-metadata',
    description: 'group inference with no group-policy override (monetary bank group)',
    input: { purpose: 'business', type: 'asset', group: 'bank accounts' },
  },
  {
    id: 'fx-group-policy-override', unit: 'resolveGroupFxPolicy', folder: 'fx-metadata',
    description: 'explicit group-policy overrides inferred fallback fields',
    input: { purpose: 'business', type: 'asset', group: 'bank accounts', groupPolicy: { fxTreatment: 'historical', closingRevalue: false } },
  },
  {
    id: 'fx-account-policy-override', unit: 'resolveAccountFxPolicy', folder: 'fx-metadata',
    description: 'account override beats group fallback (account wins)',
    input: { purpose: 'business', account: { type: 'asset', group: 'bank accounts', fxRateTimingDefault: 'valuation-date' } },
  },
  {
    id: 'fx-account-policy-fallback', unit: 'resolveAccountFxPolicy', folder: 'fx-metadata',
    description: 'no account override -> group fallback (monetary bank group)',
    input: { purpose: 'business', account: { type: 'asset', group: 'bank accounts' } },
  },
];

// ─── dispatch: execute the OLD implementation, capture the outcome ────────────
function runCase(c: Case): Any {
  const inp = c.input as Any;
  switch (c.unit) {
    case 'assertPostingAccountsValid': {
      const accountById = new Map((inp.accounts as Any[]).map((a) => [a.id as number, toAccount(a)]));
      const references = (inp.references as Any[]).map((r) => ({ accountId: r.accountId as number, txDate: new Date(r.txDate as string), source: r.source as string | undefined }));
      try { assertPostingAccountsValid({ purpose: inp.purpose as 'business' | 'personal', accountById, references }); return { ok: true }; }
      catch (e) { return { error: serializeError(e) }; }
    }
    case 'assertPostingBusinessPartnersValid': {
      const businessPartnerById = new Map((inp.businessPartners as Any[]).map((a) => [a.id as number, toTimebound(a)]));
      const references = (inp.references as Any[]).map((r) => ({ businessPartnerId: r.businessPartnerId as number, txDate: new Date(r.txDate as string), source: r.source as string | undefined }));
      try { assertPostingBusinessPartnersValid({ purpose: inp.purpose as 'business' | 'personal', businessPartnerById, references }); return { ok: true }; }
      catch (e) { return { error: serializeError(e) }; }
    }
    case 'assertPostingUsersValid': {
      const userById = new Map((inp.users as Any[]).map((a) => [a.id as number, toTimebound(a)]));
      const references = (inp.references as Any[]).map((r) => ({ userId: r.userId as number, txDate: new Date(r.txDate as string), source: r.source as string | undefined }));
      try { assertPostingUsersValid({ purpose: inp.purpose as 'business' | 'personal', userById, references }); return { ok: true }; }
      catch (e) { return { error: serializeError(e) }; }
    }
    case 'assertPostingProjectsValid': {
      const projectById = new Map((inp.projects as Any[]).map((p) => [p.id as number, toProject(p)]));
      const references = (inp.references as Any[]).map((r) => ({ projectId: r.projectId as number, txDate: new Date(r.txDate as string), source: r.source as string | undefined }));
      try { assertPostingProjectsValid({ projectById, references }); return { ok: true }; }
      catch (e) { return { error: serializeError(e) }; }
    }
    case 'assertPostingCurrencyPolicyValid': {
      const accountById = new Map((inp.accounts as Any[]).map((a) => [a.id as number, { id: a.id, currency: a.currency, type: a.type } as Any]));
      const references = (inp.references as Any[]).map((r) => ({ accountId: r.accountId as number, txCurrency: r.txCurrency as string, source: r.source as string | undefined }));
      try { assertPostingCurrencyPolicyValid({ accountById: accountById as never, references }); return { ok: true }; }
      catch (e) { return { error: serializeError(e) }; }
    }
    case 'getEffectivePeriodState': {
      const periods = (inp.periods as Any[]).map(toPeriod);
      return { value: getEffectivePeriodState(new Date(inp.txDate as string), periods as never) };
    }
    case 'assertPostingPeriodOpen': {
      const periods = (inp.periods as Any[]).map(toPeriod);
      try { assertPostingPeriodOpen(new Date(inp.txDate as string), periods as never); return { ok: true }; }
      catch (e) { return { error: serializeError(e) }; }
    }
    case 'buildTransactionLineFxMetadata': {
      const meta = buildTransactionLineFxMetadata(inp.purpose as 'business' | 'personal', inp.account as never, new Date(inp.txDate as string));
      return {
        value: {
          fxRateDate: isoDate(meta.fxRateDate),
          fxRateTiming: meta.fxRateTiming,
          fxTreatmentApplied: meta.fxTreatmentApplied,
          fxClosingRevalueApplied: meta.fxClosingRevalueApplied,
          fxVatMethodApplied: meta.fxVatMethodApplied,
          fxCurrency: meta.fxCurrency,
        },
      };
    }
    case 'resolveAccountFxPolicy': {
      const p = resolveAccountFxPolicy(inp.purpose as 'business' | 'personal', inp.account as never, inp.groupPolicy as never);
      return { value: { fxTreatment: p.fxTreatment, fxRateTimingDefault: p.fxRateTimingDefault, closingRevalue: p.closingRevalue, vatFxMethodOverride: p.vatFxMethodOverride } };
    }
    case 'resolveGroupFxPolicy': {
      const p = resolveGroupFxPolicy(inp.purpose as 'business' | 'personal', inp.type as never, inp.group as string, inp.groupPolicy as never);
      return { value: { fxTreatment: p.fxTreatment, fxRateTimingDefault: p.fxRateTimingDefault, closingRevalue: p.closingRevalue, vatFxMethodOverride: p.vatFxMethodOverride } };
    }
    default:
      throw new Error(`Unknown unit: ${c.unit}`);
  }
}

// ─── write artifacts ──────────────────────────────────────────────────────────
function outDir(): string {
  const override = process.env.LL_FIXTURE_OUT;
  if (override) return override;
  // Default: new repo is a sibling folder named "LeafLedger" next to the old repo.
  return path.resolve(process.cwd(), '..', 'LeafLedger', 'tests', 'fixtures', 'golden', 'ledger-core');
}
function stableJson(value: unknown): string {
  return JSON.stringify(value, null, 2) + '\n';
}
function oldRepoSha(): string {
  return execSync('git rev-parse HEAD', { cwd: process.cwd() }).toString().trim();
}

it('captures ledger-core golden fixtures from the OLD implementation', () => {
  const root = outDir();
  const seenIds = new Set<string>();
  const manifestCases: Array<{ id: string; unit: string; description: string; file: string }> = [];

  // fresh-write only the per-unit case folders (leave SOURCE.md / fixture-format.md untouched)
  for (const folder of ['posting-accounts', 'posting-business-partners', 'posting-users', 'posting-projects', 'currency-policy', 'period-state', 'fx-metadata']) {
    rmSync(path.join(root, folder), { recursive: true, force: true });
    mkdirSync(path.join(root, folder), { recursive: true });
  }

  for (const c of cases) {
    if (seenIds.has(c.id)) throw new Error(`Duplicate case id: ${c.id}`);
    seenIds.add(c.id);

    const expected = runCase(c);
    const rel = `${c.folder}/${c.id}.json`;
    writeFileSync(path.join(root, rel), stableJson({ input: c.input, expected }), 'utf8');
    manifestCases.push({ id: c.id, unit: c.unit, description: c.description, file: rel });
  }

  writeFileSync(
    path.join(root, 'manifest.json'),
    stableJson({
      source: { repo: 'Lokkeccs/Accounting', sha: oldRepoSha() },
      count: manifestCases.length,
      cases: manifestCases,
    }),
    'utf8',
  );
});
