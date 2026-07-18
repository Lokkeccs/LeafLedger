import { createBrowserRouter, createMemoryRouter } from 'react-router-dom'
import { AppLayout } from './AppLayout'
import { HomeRoute } from './HomeRoute'
import { NotFoundRoute } from './NotFoundRoute'
import { RouteErrorBoundary } from './RouteErrorBoundary'

export function createAppRouter(initialEntry?: string) {
	const routes = [{ path: '/', element: <AppLayout />, errorElement: <RouteErrorBoundary />, children: [{ index: true, lazy: async () => ({ Component: HomeRoute }) }, { path: 'accounts', lazy: async () => ({ Component: (await import('../features/accounts/AccountsPage')).AccountsPage }) }, { path: 'reports/trial-balance', lazy: async () => ({ Component: (await import('../features/reports/TrialBalancePage')).TrialBalancePage }) }, { path: 'reports/balance-sheet', lazy: async () => ({ Component: (await import('../features/reports/BalanceSheetPage')).BalanceSheetPage }) }, { path: 'reports/income-statement', lazy: async () => ({ Component: (await import('../features/reports/IncomeStatementPage')).IncomeStatementPage }) }, { path: 'reports/account', lazy: async () => ({ Component: (await import('../features/reports/AccountLedgerPage')).AccountLedgerPage }) }, { path: 'reports/account/:accountId', lazy: async () => ({ Component: (await import('../features/reports/AccountLedgerPage')).AccountLedgerPage }) }, { path: 'journal-entries/new', lazy: async () => ({ Component: (await import('../features/journal-entry/JournalEntryPage')).JournalEntryPage }) }, { path: 'design', lazy: async () => ({ Component: (await import('../features/design/DesignSystemPage')).DesignSystemPage }) }, { path: '*', Component: NotFoundRoute }] }]
	return initialEntry ? createMemoryRouter(routes, { initialEntries: [initialEntry] }) : createBrowserRouter(routes)
}

export const appRouter = createAppRouter()