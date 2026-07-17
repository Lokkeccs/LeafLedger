# LeafLedger UI Design Guidelines

LeafLedger is a modern, clean, neutral-professional accounting interface. It is desktop-first, dense without being cramped, and gives numeric data enough contrast and alignment to scan quickly.

## Principles

- Use semantic roles rather than one-off colors.
- Prefer the existing shared primitives before adding feature-local controls.
- Keep layouts stable and keyboard accessible. Every interactive control needs a visible `:focus-visible` treatment.
- Amounts are right-aligned, use `--font-mono`, and use tabular numerals. Formatting remains at the render edge; the design layer performs no amount calculations.

## Token catalog

The canonical source is `app/src/shared/styles/tokens.css`. It defines:

- Color roles: background, surfaces, borders, text, primary, neutral scale, and success/warning/danger/info states.
- Typography: sans and mono families, six text sizes, weights, and line-heights.
- Layout: `--space-1` through `--space-8`, radius tokens, shadows, focus ring, z-index layers, and motion/easing tokens.
- Both `:root` and `:root[data-theme='dark']` values. The theme runtime persists explicit choices under `ll.theme`; system mode follows `prefers-color-scheme`.

## Usage rules

Use `var(--token-name)` in CSS and inline style objects. Never introduce hex, RGB, HSL, or ad-hoc spacing values in `app/src/**`; the design-token check is a blocking gate. The token file and `/design` gallery are the only intentional exceptions.

Normal text and semantic foreground/background combinations target WCAG AA contrast of at least 4.5:1 in both themes. Use the subtle semantic background with its matching semantic foreground, not a raw hue on an arbitrary surface.

Avoid nested cards, decorative gradients, and extra responsive-table machinery. Use full-width page bands and framed cards only for repeated items, modals, and genuinely framed tools. The live reference for tokens and shared primitives is available at the in-app `/design` route.