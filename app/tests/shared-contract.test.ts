import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const sharedRoot = resolve('src/shared')
const readShared = (file: string) => readFileSync(resolve(sharedRoot, file), 'utf8')

describe('P3-WP04 shared primitive contracts', () => {
  it('defines and imports the canonical token layer', () => {
    const tokens = readShared('styles/tokens.css')
    const shell = readFileSync(resolve('src/index.css'), 'utf8')
    for (const token of ['--color-ink', '--color-muted', '--color-paper', '--color-surface', '--color-line', '--color-accent', '--color-focus', '--color-success', '--color-warning', '--color-danger', '--color-info', '--space-1', '--space-2', '--space-3', '--space-4', '--space-5', '--radius-sm', '--radius-md', '--radius-lg', '--shadow-sm', '--shadow-md', '--shadow-strong', '--font-sans', '--font-heading']) {
      expect(tokens).toContain(token)
    }
    expect(shell.match(/@import ['"]\.\/shared\/styles\/tokens\.css['"]/g)).toHaveLength(1)
    for (const alias of ['--ink: var(--color-ink)', '--muted: var(--color-muted)', '--paper: var(--color-paper)', '--surface: var(--color-surface)', '--line: var(--color-line)', '--accent: var(--color-accent)', '--focus: var(--color-focus)', '--shadow: var(--shadow-md)']) {
      expect(shell).toContain(alias)
    }
  })

  it('pins the desktop-only and integer-safe implementation boundaries', () => {
    const table = readShared('DataTable.tsx')
    const money = readShared('MoneyInput.tsx')
    expect(table).not.toMatch(/useTableBreakpoint|useIsTightTableScreen|compactCard|matchMedia/)
    expect(money).not.toMatch(/parseFloat|\/\s*100|\*\s*100/)
  })

  it('keeps deferred responsive, preference, theme, and domain modules out of shared', () => {
    for (const file of ['useTableBreakpoint.ts', 'NumberFormatContext.tsx', 'DateFormatContext.tsx', 'useNumberFormat.ts', 'useDateFormat.ts', 'ThemeContext.tsx', 'useTheme.ts', 'AccountPicker.tsx', 'useRecentAccounts.ts']) {
      expect(existsSync(resolve(sharedRoot, file))).toBe(false)
    }
    for (const file of ['DataTable.tsx', 'MoneyInput.tsx', 'ModalShell.tsx', 'FormField.tsx']) {
      expect(readShared(file)).not.toMatch(/\bfetch\s*\(/)
    }
  })
})