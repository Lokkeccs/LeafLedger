# SOURCE - period-lifecycle golden fixtures

These fixtures pin the OLD period transition and date-boundary behavior needed by
P2-WP10. Expected values were captured by executing the OLD implementation with
the committed harness at [tools/fixtures/period-lifecycle/capture.test.ts](../../../../../tools/fixtures/period-lifecycle/capture.test.ts).

## Old-repo pin

| | |
|---|---|
| Repo | `Lokkeccs/Accounting` |
| Commit SHA | `085bedba467e3d46d3889db3bc80ea023e69756e` |
| Local checkout | `C:\\Programming\\LeafLedger\\Accounting` |
| Captured | 2026-07-12 |
| Capture timezone | `TZ=UTC` |
| Case count | 12 |

## Source references

| Symbol | Source | Lines |
|---|---|---|
| `updatePeriodState` | `src/features/admin/view-model/adminPeriodDataApi.ts` | 84-111 |
| `getPeriodForDate` | `src/shared/periodUtils.ts` | 122-130 |

## Coverage notes

- All nine combinations of `open`, `closed`, and `locked` initial/requested states
  are captured through OLD `updatePeriodState`.
- OLD returns `true` for `open -> closed`, `closed -> open`, same-state
  `open|closed` requests, and runtime `open|closed -> locked` requests. It
  returns `false` for every request from `locked`. The TypeScript signature of
  `updatePeriodState` excludes `locked`; the two `-> locked` cases therefore
  capture the runtime behavior used by the target's privileged lock decision,
  while OLD's normal lock path remains the separate system-only function.
- The date lookup captures the inclusive start, inclusive end at
  `23:59:59.999`, and the first date after the inclusive end.
- The `closedAt` timestamp is intentionally reduced to the stable
  `closedAtPresent` behavior because OLD writes the wall clock via `new Date()`.
- No cases are unverified.

The target WP10 policy permits an explicit Owner/Admin lock command. That is a
new target-surface decision and is not represented as an OLD transition oracle.
