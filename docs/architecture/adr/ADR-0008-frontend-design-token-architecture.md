# ADR-0008: Frontend design-token architecture — CSS custom properties with light/dark themes

- **Status:** accepted
- **Date:** 2026-07-17
- **Deciders:** User + LL Architect (P4-WP01); implementation by LL Frontend Dev
- **Related:** WP P4-WP01 ([plan](../../rebuild/plans/P4-WP01-design-system-visual-identity.md)); P3-WP04 ([plan](../../rebuild/plans/P3-WP04-shared-ui-primitives.md), D-P3-UI-TOKENS, D-P3-UI-THEME); target architecture Part 3 §7 (shared UI primitives and frontend layering); quality Part 5 (frontend gates)

## Context

LeafLedger's Phase-3 shared UI primitives established `app/src/shared/styles/tokens.css` as the styling seam, but theming was deliberately deferred to Phase 4. The remaining M1 feature pages need a consistent visual language before they are built: modern, clean, neutral-professional, desktop-first, and legible for dense accounting tables.

The design foundation must support light and dark themes from day one, preserve the existing shared primitives and their public APIs, avoid a new runtime component-library dependency, and prevent later pages from drifting into hardcoded colors and spacing. Theme preference is UI state, not user/accounting data, and should therefore survive sign-out in the same way as the persisted i18n language preference.

Without a durable architecture decision, later feature WPs could introduce parallel styling systems, inconsistent theme behavior, or page-local literals that are expensive to restyle and difficult to enforce mechanically.

## Decision

We will use **CSS custom properties in the single canonical `app/src/shared/styles/tokens.css` file** as LeafLedger's frontend design-token architecture.

1. **Semantic tokens are the styling API.** Color roles, typography, spacing, radius, elevation, focus, z-index, and motion are expressed as CSS custom properties. Shared primitives and shell chrome consume token variables rather than raw color or spacing literals.
2. **One token contract serves two themes.** The light theme is defined at `:root`; dark-theme overrides are defined at `:root[data-theme='dark']`. The token names remain stable while theme values change.
3. **Theme selection belongs to the app shell.** A small `ThemeProvider`/`useTheme()` runtime sets `data-theme` on `<html>`, defaults to `prefers-color-scheme`, supports an explicit light/dark/system choice, and persists the explicit choice in `localStorage`. Sign-out teardown does not clear this preference.
4. **Existing primitives are extended, not replaced.** P4-WP01 may visually re-skin P3-WP04 primitives and `AppLayout`, but it must preserve their public props and behavior. No CSS-in-JS runtime or component-library dependency is introduced for theming.
5. **The system is documented and inspectable.** P4-WP01 provides an in-app `/design` gallery and `docs/architecture/rebuild/ui-design-guidelines.md` as the living reference for later feature WPs.
6. **Token usage is a quality gate.** `tools/check-design-tokens.cjs` and the associated frontend lint/build wiring reject hardcoded colors and raw spacing in `app/src/**` outside explicitly permitted token/gallery locations. The gate is part of the frontend quality contract, not merely a review convention.

## Consequences

- **Positive:** Later feature pages inherit a single visual language and can switch themes without page-specific branching.
- **Positive:** Existing primitives remain the reusable UI boundary; visual changes do not force feature pages to migrate component APIs.
- **Positive:** CSS custom properties keep theme changes cheap at runtime and avoid adding a UI framework or CSS-in-JS bundle cost.
- **Positive:** The gallery and guideline document make the system concrete for implementers and reviewers; the enforcement gate catches common drift automatically.
- **Positive (privacy/lifecycle boundary):** Theme preference is local UI state and survives sign-out, while accounting and space data remain subject to normal teardown and authorization rules.
- **Neutral:** Token values may evolve as the visual system is refined, but token names and semantic meaning should remain stable. A future breaking token-contract change requires an explicit architecture update.
- **Negative:** CSS custom properties do not provide compile-time validation of every token use; the token contract tests, lint, and red-gate are required complements.
- **Negative:** The gallery, contrast checks, and two-theme coverage add maintenance work to frontend WPs, but they make visual regressions visible before feature pages accumulate.
- **Scope boundary:** Responsive phone layouts, user-facing date/number-format preferences, brand-asset production, and MSIX packaging remain outside this decision and follow their Phase-4 WPs.

## Alternatives considered

- **A component-library theming system.** Rejected for P4-WP01: it adds a runtime dependency and would require migrating the P3-WP04 primitives before the M1 feature work. The existing primitives plus CSS custom properties are sufficient.
- **CSS-in-JS.** Rejected: it adds runtime and tooling complexity without improving the current primitive boundary, and would make the design tokens less directly inspectable by the token gate.
- **Separate token files or per-feature tokens.** Rejected: multiple sources of truth would make cross-page consistency and enforcement harder. `tokens.css` remains canonical.
- **JavaScript-only theme values.** Rejected: CSS custom properties allow browser-native cascading, low-cost theme switching, and styling of existing CSS modules without duplicating values in React.
- **Reset the theme on sign-out.** Rejected: theme is a local UI preference, not space/user data; resetting it would make a shared-machine experience unnecessarily surprising. Persistence mirrors D-P3-I18N-PERSIST.
- **Code review without an automated gate.** Rejected: review alone will not reliably prevent hardcoded values across the remaining M1 pages. The design-token red-gate is the mechanical backstop.
