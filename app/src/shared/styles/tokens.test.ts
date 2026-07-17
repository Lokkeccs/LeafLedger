import fs from 'node:fs'
import path from 'node:path'
import { describe, expect, it } from 'vitest'

const css = fs.readFileSync(path.resolve(process.cwd(), 'src/shared/styles/tokens.css'), 'utf8')

function block(selector: string) {
  const match = css.match(new RegExp(`${selector} \\{([\\s\\S]*?)\\n\\}`))
  if (!match) throw new Error(`Missing token block: ${selector}`)
  return match[1]!
}

function tokenValue(source: string, name: string) {
  const match = source.match(new RegExp(`--${name}:\\s*([^;]+);`))
  if (!match) throw new Error(`Missing token: ${name}`)
  return match[1]!.trim()
}

function hexRgb(value: string) {
  const hex = value.replace('#', '')
  const expanded = hex.length === 3 ? hex.split('').map((digit) => digit + digit).join('') : hex
  return [0, 2, 4].map((offset) => Number.parseInt(expanded.slice(offset, offset + 2), 16) / 255)
}

function luminance(value: string) {
  return hexRgb(value).map((channel) => channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4).reduce((sum, channel, index) => sum + channel * [0.2126, 0.7152, 0.0722][index]!, 0)
}

function contrast(foreground: string, background: string) {
  const light = Math.max(luminance(foreground), luminance(background))
  const dark = Math.min(luminance(foreground), luminance(background))
  return (light + 0.05) / (dark + 0.05)
}

describe('design token contract', () => {
  it('declares the required families and distinct theme representatives', () => {
    const light = block(':root')
    const dark = block(":root\\[data-theme='dark'\\]")
    for (const name of ['color-bg', 'color-surface', 'color-primary', 'color-success', 'color-warning', 'color-danger', 'color-info', 'font-sans', 'text-base', 'space-4', 'radius-md', 'shadow-md', 'focus-ring', 'z-modal']) {
      expect(light).toContain(`--${name}:`)
    }
    expect(tokenValue(light, 'color-bg')).not.toBe(tokenValue(dark, 'color-bg'))
    expect(tokenValue(light, 'color-primary')).not.toBe(tokenValue(dark, 'color-primary'))
  })

  it('keeps text and semantic foregrounds AA-readable in both themes', () => {
    for (const selector of [':root', ":root\\[data-theme='dark'\\]"]) {
      const source = block(selector)
      expect(contrast(tokenValue(source, 'color-text'), tokenValue(source, 'color-bg'))).toBeGreaterThanOrEqual(4.5)
      expect(contrast(tokenValue(source, 'color-text'), tokenValue(source, 'color-surface'))).toBeGreaterThanOrEqual(4.5)
      for (const semantic of ['success', 'warning', 'danger', 'info']) {
        expect(contrast(tokenValue(source, `color-${semantic}`), tokenValue(source, `color-${semantic}-subtle`))).toBeGreaterThanOrEqual(4.5)
      }
    }
  })
})