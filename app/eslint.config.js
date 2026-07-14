import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  // src/api is the GENERATED OpenAPI client (P1-WP04) — never hand-edited, never linted.
  globalIgnores(['dist', 'src/api/**']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    rules: {
      '@typescript-eslint/no-unused-vars': ['error', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_',
        caughtErrorsIgnorePattern: '^_',
      }],
    },
  },

  // ── Boundary: features → application → api ────────────────────────────────
  // Feature pages/components talk ONLY to the application layer.
  {
    files: ['src/features/**/*.ts', 'src/features/**/*.tsx'],
    rules: {
      'no-restricted-imports': ['error', {
        patterns: [
          {
            group: ['**/api/**'],
            message: 'Architecture enforcement: features must not import the generated API client directly. Go through application-layer hooks (features → application → api).',
          },
          {
            group: ['**/app/**'],
            message: 'Architecture enforcement: features must not depend on app shell internals.',
          },
        ],
      }],
    },
  },
  // The application layer must not depend on UI layers.
  {
    files: ['src/application/**/*.ts', 'src/application/**/*.tsx'],
    rules: {
      'no-restricted-imports': ['error', {
        patterns: [
          {
            group: ['**/features/**'],
            message: 'Architecture enforcement: the application layer must not depend on feature UI.',
          },
          {
            group: ['**/app/**'],
            message: 'Architecture enforcement: the application layer must not depend on the app shell.',
          },
        ],
      }],
    },
  },
  {
    files: ['src/shared/**/*.ts', 'src/shared/**/*.tsx'],
    rules: {
      'no-restricted-imports': ['error', {
        patterns: [{
          group: ['**/features/**', '**/app/**', '**/application/**', '**/api/**'],
          message: 'Architecture enforcement: shared UI primitives must remain a presentation leaf.',
        }],
      }],
    },
  },

  // ── No direct fetch outside the generated client ──────────────────────────
  {
    files: ['src/**/*.ts', 'src/**/*.tsx'],
    ignores: ['src/api/**'],
    rules: {
      'no-restricted-globals': ['error', {
        name: 'fetch',
        message: 'Architecture enforcement: all HTTP goes through the generated client in src/api (used via application-layer hooks).',
      }],
      'no-restricted-properties': ['error',
        { object: 'window', property: 'fetch', message: 'Architecture enforcement: all HTTP goes through the generated client in src/api.' },
        { object: 'globalThis', property: 'fetch', message: 'Architecture enforcement: all HTTP goes through the generated client in src/api.' },
      ],
    },
  },
])
