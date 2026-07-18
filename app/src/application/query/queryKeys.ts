export const qk = {
  meta: () => ['meta'] as const,
  accounts: { list: (spaceId: string) => ['accounts', 'list', spaceId] as const },
  journalEntries: { list: (spaceId: string) => ['journalEntries', 'list', spaceId] as const },
  reports: {
    trialBalance: (spaceId: string) => ['reports', 'trialBalance', spaceId] as const,
    balanceSheet: (spaceId: string) => ['reports', 'balanceSheet', spaceId] as const,
    incomeStatement: (spaceId: string) => ['reports', 'incomeStatement', spaceId] as const,
    accountLedger: (spaceId: string, accountId: string, from?: string, to?: string) => ['reports', 'accountLedger', spaceId, accountId, from ?? null, to ?? null] as const,
  },
} as const