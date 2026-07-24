export const qk = {
  meta: () => ['meta'] as const,
  accounts: { list: (spaceId: string) => ['accounts', 'list', spaceId] as const },
  businessPartners: { list: (spaceId: string) => ['businessPartners', 'list', spaceId] as const },
  accountGroups: { list: (spaceId: string) => ['accountGroups', 'list', spaceId] as const },
  journalEntries: { list: (spaceId: string) => ['journalEntries', 'list', spaceId] as const },
  reports: {
    trialBalance: (spaceId: string) => ['reports', 'trialBalance', spaceId] as const,
    balanceSheet: (spaceId: string) => ['reports', 'balanceSheet', spaceId] as const,
    incomeStatement: (spaceId: string) => ['reports', 'incomeStatement', spaceId] as const,
    dashboard: (spaceId: string) => ['reports', 'dashboard', spaceId] as const,
    accountLedger: (spaceId: string, accountId: string, from?: string, to?: string) => ['reports', 'accountLedger', spaceId, accountId, from ?? null, to ?? null] as const,
  },
} as const