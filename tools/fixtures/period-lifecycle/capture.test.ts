/**
 * P2-WP10 - Golden fixture capture harness.
 *
 * Runs the OLD period transition and lookup functions. The Dexie table is
 * mocked only at the data boundary so updatePeriodState itself is executed.
 */

import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { beforeEach, describe, expect, it, vi } from 'vitest';

interface PeriodRow {
  id: number;
  name: string;
  startDate: Date;
  endDate: Date;
  state: 'open' | 'closed' | 'locked';
  updatedAt: Date;
  closedAt?: Date;
  closedBy?: number;
}

const rows = new Map<number, PeriodRow>();
const accountingPeriods = {
  get: vi.fn(async (id: number) => rows.get(id)),
  update: vi.fn(async (id: number, patch: Partial<PeriodRow>) => {
    const row = rows.get(id);
    if (row) Object.assign(row, patch);
    return 1;
  }),
};

vi.doMock('../src/data/db', () => ({
  db: { accountingPeriods },
  getActiveAccountingSpace: vi.fn(),
}));

const { adminPeriodDataApi } = await import('../src/features/admin/view-model/adminPeriodDataApi');
const { getPeriodForDate } = await import('../src/shared/periodUtils');

const outputRoot = process.env.LL_FIXTURE_OUT
  ? path.resolve(process.env.LL_FIXTURE_OUT)
  : path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../tests/fixtures/golden/ledger-core/period-lifecycle');

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function stableJson(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

function periodRow(state: PeriodRow['state']): PeriodRow {
  return {
    id: 1,
    name: 'January 2026',
    startDate: new Date('2026-01-01T00:00:00.000Z'),
    endDate: new Date('2026-01-31T23:59:59.999Z'),
    state,
    updatedAt: new Date(0),
  };
}

const transitionCases = [
  ['open-to-open', 'open', 'open'],
  ['open-to-closed', 'open', 'closed'],
  ['open-to-locked', 'open', 'locked'],
  ['closed-to-open', 'closed', 'open'],
  ['closed-to-closed', 'closed', 'closed'],
  ['closed-to-locked', 'closed', 'locked'],
  ['locked-to-open', 'locked', 'open'],
  ['locked-to-closed', 'locked', 'closed'],
  ['locked-to-locked', 'locked', 'locked'],
] as const;

const boundaryCases = [
  ['at-start', '2026-01-01T00:00:00.000Z'],
  ['at-inclusive-end', '2026-01-31T23:59:59.999Z'],
  ['after-inclusive-end', '2026-02-01T00:00:00.000Z'],
] as const;

beforeEach(() => {
  rows.clear();
  accountingPeriods.get.mockClear();
  accountingPeriods.update.mockClear();
});

describe('OLD period lifecycle capture', () => {
  for (const [id, initialState, newState] of transitionCases) {
    it(id, async () => {
      rows.set(1, periodRow(initialState));
      const result = await adminPeriodDataApi.updatePeriodState(1, newState, 42);
      const row = rows.get(1)!;
      const expected = {
        value: {
          result,
          state: row.state,
          closedAtPresent: row.closedAt !== undefined,
          closedBy: row.closedBy,
        },
      };
      const output = { input: { periodId: 1, initialState, newState, closedBy: 42 }, expected };
      mkdirSync(path.join(outputRoot, 'period-transitions'), { recursive: true });
      writeFileSync(path.join(outputRoot, 'period-transitions', `${id}.json`), stableJson(output), 'utf8');
      expect(result).toBeTypeOf('boolean');
    });
  }

  for (const [id, date] of boundaryCases) {
    it(id, () => {
      const periods = [{
        name: 'January 2026',
        startDate: new Date('2026-01-01T00:00:00.000Z'),
        endDate: new Date('2026-01-31T23:59:59.999Z'),
        state: 'open' as const,
      }];
      const result = getPeriodForDate(new Date(date), periods);
      const expected = { value: result ? {
        name: result.name,
        startDate: isoDate(result.startDate),
        endDate: isoDate(result.endDate),
        state: result.state,
      } : null };
      const output = { input: { date: date.slice(0, 10), periods: [{ name: 'January 2026', startDate: '2026-01-01', endDate: '2026-01-31', state: 'open' }] }, expected };
      mkdirSync(path.join(outputRoot, 'period-boundaries'), { recursive: true });
      writeFileSync(path.join(outputRoot, 'period-boundaries', `${id}.json`), stableJson(output), 'utf8');
      expect(result?.name ?? null).toBe(id === 'after-inclusive-end' ? null : 'January 2026');
    });
  }
});
