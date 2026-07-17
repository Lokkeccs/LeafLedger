#!/usr/bin/env node
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', 'app', 'src');
const ignored = new Set(['shared/styles/tokens.css']);
const colorLiteral = /#[0-9a-f]{3,8}\b|\b(?:rgb|rgba|hsl|hsla)\s*\(/i;
const inlineSpacingLiteral = /style\s*=\s*\{\{[^}]*\b(?:padding|margin|gap|inset|top|right|bottom|left|width|height|gridTemplateColumns)\s*:\s*[^,}]*\b\d+px\b/i;

function walk(dir, files = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'dist') continue;
    const absolute = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(absolute, files);
    else if (/\.(css|ts|tsx)$/.test(entry.name)) files.push(absolute);
  }
  return files;
}

function scanSource(source, file) {
  if (ignored.has(file.replaceAll('\\', '/')) || file.startsWith('features/design/')) return [];
  return source.split(/\r?\n/).flatMap((line, index) => {
    if (colorLiteral.test(line) || inlineSpacingLiteral.test(line)) return [{ file, line: index + 1, text: line.trim() }];
    return [];
  });
}

function scan(rootDir = root) {
  return walk(rootDir).flatMap((file) => {
    const relative = path.relative(rootDir, file).replaceAll('\\', '/');
    return scanSource(fs.readFileSync(file, 'utf8'), relative);
  });
}

if (require.main === module) {
  const violations = scan();
  if (violations.length) {
    console.error('Design-token check failed: hardcoded colors or inline spacing found outside tokens.css/gallery.');
    for (const violation of violations) console.error(`${violation.file}:${violation.line} ${violation.text}`);
    process.exit(1);
  }
  console.log('Design-token check passed.');
}

module.exports = { scan, scanSource };