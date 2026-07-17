// @vitest-environment jsdom
import { act, render, screen } from '@testing-library/react'
import { QueryClient } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ThemeProvider, useTheme } from './ThemeProvider'

function Probe() {
  const { resolvedTheme, setTheme } = useTheme()
  return <button type="button" onClick={() => setTheme('dark')}>{resolvedTheme}</button>
}

describe('theme runtime', () => {
  beforeEach(() => { localStorage.clear(); delete document.documentElement.dataset.theme })

  it('applies and persists an explicit theme without depending on query state', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    expect(document.documentElement.dataset.theme).toBe('light')
    act(() => screen.getByRole('button').click())
    expect(document.documentElement.dataset.theme).toBe('dark')
    expect(localStorage.getItem('ll.theme')).toBe('dark')
  })

  it('follows the system preference when no explicit choice exists', () => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
    render(<ThemeProvider><Probe /></ThemeProvider>)
    expect(document.documentElement.dataset.theme).toBe('dark')
    vi.unstubAllGlobals()
  })

  it('keeps the explicit preference when application query state is cleared', () => {
    render(<ThemeProvider><Probe /></ThemeProvider>)
    act(() => screen.getByRole('button').click())
    new QueryClient().clear()
    expect(localStorage.getItem('ll.theme')).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
  })
})