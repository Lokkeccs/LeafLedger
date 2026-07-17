// @vitest-environment jsdom
import { render, screen, cleanup } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { afterEach, describe, expect, it } from 'vitest'
import { i18n } from '../../i18n'
import { DesignSystemPage } from './DesignSystemPage'

afterEach(() => {
  cleanup()
  delete document.documentElement.dataset.theme
})

describe('design system gallery', () => {
  it.each(['light', 'dark'] as const)('renders the complete gallery in %s mode', (theme) => {
    document.documentElement.dataset.theme = theme
    render(<I18nextProvider i18n={i18n}><DesignSystemPage /></I18nextProvider>)
    expect(screen.getByRole('heading', { name: 'Design system' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Shared primitives' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Open modal' })).toBeTruthy()
    expect(screen.getByText('Light theme')).toBeTruthy()
    expect(screen.getByText('Dark theme')).toBeTruthy()
  })
})