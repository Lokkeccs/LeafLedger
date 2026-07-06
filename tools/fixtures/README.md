# Golden fixture capture harness

Committed harnesses that (re-)produce the language-neutral golden fixtures under
`tests/fixtures/golden/**` by **executing the OLD (read-only) Accounting
implementation**. Expected values are never hand-authored — they are captured
from real old-code execution so the new C# domain and the TS pre-validation
mirror can be graded "to the branch".

## ledger-core (P2-WP01)

`ledger-core/capture.test.ts` pins the Phase-2 posting rules from the old repo
[`Lokkeccs/Accounting`](https://github.com/Lokkeccs/Accounting):

- `src/shared/postingValidity.ts` — posting-validity + currency-policy + period-open guards
- `src/shared/periodUtils.ts` — `getEffectivePeriodState`
- `src/shared/fxPolicy.ts` — FX-policy resolution + line FX metadata

Output: `tests/fixtures/golden/ledger-core/**` (+ `manifest.json`).

### How to re-capture (reproducible)

The harness is committed here but must run inside the **old repo's own toolchain**
(vitest, TS config, Node `environment: 'node'`). It is copied into the old repo's
`tests/` folder, executed under **UTC**, then the temporary copy is deleted so the
old repo stays pristine. From PowerShell:

```powershell
$old = "C:\Programming\LeafLedger\Accounting"      # read-only reference checkout
$new = "C:\Programming\LeafLedger\LeafLedger"       # this repo

# 1. deterministic date math (future/expired boundaries) + explicit output dir
$env:TZ = "UTC"
$env:LL_FIXTURE_OUT = "$new\tests\fixtures\golden\ledger-core"

# 2. copy the committed harness into the old repo's tests/ (matches its include glob)
Copy-Item "$new\tools\fixtures\ledger-core\capture.test.ts" "$old\tests\_goldenCapture.test.ts"

# 3. run against the old implementation
Push-Location $old
npx vitest run tests/_goldenCapture.test.ts
Pop-Location

# 4. remove the temporary file — the old repo must stay pristine
Remove-Item "$old\tests\_goldenCapture.test.ts"
```

Re-running against the pinned SHA (recorded in the fixture set's `SOURCE.md` and
`manifest.json`) reproduces byte-identical artifacts: stable key order, 2-space
indent + trailing newline, no wall-clock values, ISO `yyyy-MM-dd` dates only.

Notes:
- `LL_FIXTURE_OUT` is optional; without it the harness defaults to a sibling
  folder named `LeafLedger` next to the old repo.
- The harness rewrites only the per-unit case folders and `manifest.json`; the
  hand-authored `SOURCE.md` and `fixture-format.md` are left untouched.
