import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

export type Theme = 'light' | 'dark' | 'system'
type ResolvedTheme = Exclude<Theme, 'system'>
type ThemeContextValue = { theme: Theme; resolvedTheme: ResolvedTheme; setTheme: (theme: Theme) => void }

const storageKey = 'll.theme'
const ThemeContext = createContext<ThemeContextValue | null>(null)

function systemTheme(): ResolvedTheme {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function storedTheme(): Theme {
  const value = typeof window !== 'undefined' ? window.localStorage.getItem(storageKey) : null
  return value === 'light' || value === 'dark' || value === 'system' ? value : 'system'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(storedTheme)
  const resolvedTheme = theme === 'system' ? systemTheme() : theme

  useEffect(() => {
    document.documentElement.dataset.theme = resolvedTheme
  }, [resolvedTheme, theme])

  useEffect(() => {
    window.localStorage.setItem(storageKey, theme)
  }, [theme])

  const value = useMemo(() => ({ theme, resolvedTheme, setTheme: (next: Theme) => setThemeState(next) }), [theme, resolvedTheme])
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

// The provider and its hook intentionally share this small module as the theme boundary.
// eslint-disable-next-line react-refresh/only-export-components
export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext)
  if (context) return context
  const resolvedTheme = document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light'
  return { theme: resolvedTheme, resolvedTheme, setTheme: (next) => { document.documentElement.dataset.theme = next === 'dark' ? 'dark' : 'light' } }
}