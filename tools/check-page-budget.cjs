#!/usr/bin/env node
// Ported from the old repo's scripts/check-page-budget.cjs (behavior preserved).
// Adaptations for the rebuild: baseline lives next to this script (starts empty),
// and the script runs against the frontend package it is invoked from (app/).
const fs = require('node:fs');
const path = require('node:path');

const ROOT = process.cwd();
const STRICT = process.argv.includes('--strict');
const UPDATE_BASELINE = process.argv.includes('--update-baseline');
const BASELINE_PATH = path.join(__dirname, 'page-budget-baseline.json');

const WARN_LINE_MAX = 450;
const ERROR_LINE_MAX = 550;
const WARN_IMPORT_MAX = 30;
const ERROR_IMPORT_MAX = 40;
const WARN_STATE_MAX = 20;
const ERROR_STATE_MAX = 28;

const targets = [];

function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'dist' || entry.name.startsWith('.')) continue;
    const abs = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(abs);
      continue;
    }
    if (!entry.name.endsWith('.tsx')) continue;
    const rel = path.relative(ROOT, abs).replaceAll('\\', '/');
    if (rel === 'src/app/App.tsx' || /src\/features\/.*Page\.tsx$/.test(rel)) {
      targets.push(abs);
    }
  }
}

function countImports(source) {
  return source
    .split(/\r?\n/)
    .filter((line) => /^\s*import\s+/.test(line))
    .length;
}

function countStateHooks(source) {
  const useStateCount = (source.match(/\buseState\s*\(/g) || []).length;
  const useReducerCount = (source.match(/\buseReducer\s*\(/g) || []).length;
  return useStateCount + useReducerCount;
}

function loadBaseline() {
  if (!fs.existsSync(BASELINE_PATH)) return {};
  try {
    return JSON.parse(fs.readFileSync(BASELINE_PATH, 'utf8'));
  } catch {
    return {};
  }
}

walk(path.join(ROOT, 'src'));

let hasError = false;
const rows = [];
const baseline = loadBaseline();
const nextBaseline = {};

for (const file of targets) {
  const source = fs.readFileSync(file, 'utf8');
  const lines = source.split(/\r?\n/).length;
  const imports = countImports(source);
  const states = countStateHooks(source);
  const rel = path.relative(ROOT, file).replaceAll('\\', '/');

  const lineState = lines > ERROR_LINE_MAX ? 'error' : lines > WARN_LINE_MAX ? 'warn' : 'ok';
  const importState = imports > ERROR_IMPORT_MAX ? 'error' : imports > WARN_IMPORT_MAX ? 'warn' : 'ok';
  const stateState = states > ERROR_STATE_MAX ? 'error' : states > WARN_STATE_MAX ? 'warn' : 'ok';

  const baselineRow = baseline[rel];
  const exceedsErrorThreshold = lineState === 'error' || importState === 'error' || stateState === 'error';
  const isRegression = !baselineRow
    || lines > baselineRow.lines
    || imports > baselineRow.imports
    || states > baselineRow.states;

  if (STRICT && exceedsErrorThreshold && isRegression) hasError = true;

  rows.push({ rel, lines, imports, states, lineState, importState, stateState });
  nextBaseline[rel] = { lines, imports, states };
}

rows.sort((a, b) => b.lines - a.lines);
console.log('Architecture page budget report');
for (const row of rows) {
  console.log(`${row.rel} | lines=${row.lines}(${row.lineState}) imports=${row.imports}(${row.importState}) states=${row.states}(${row.stateState})`);
}

if (UPDATE_BASELINE) {
  fs.writeFileSync(BASELINE_PATH, `${JSON.stringify(nextBaseline, null, 2)}\n`);
  console.log(`Updated baseline at ${BASELINE_PATH}`);
}

if (STRICT && hasError) {
  console.error('Page budget check failed in strict mode due to new regressions above baseline.');
  process.exit(1);
}
