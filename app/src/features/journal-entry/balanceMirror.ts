export type MirrorAccount = { id: string; currency: string; kind: string }
export type MirrorLine = { accountId: string; currency: string; amountMinor: number }
export type MirrorIssue = { code: string; line: number | null }

function fixedCurrencyKind(kind: string): boolean {
  return ['asset', 'liability', 'equity'].includes(kind.toLowerCase())
}

export function currencyPolicyAllows(account: MirrorAccount | undefined, transactionCurrency: string): boolean {
  if (!account || !account.currency || !transactionCurrency) return true
  return !fixedCurrencyKind(account.kind) || account.currency.toUpperCase() === transactionCurrency.toUpperCase()
}

export function balanceMirror(description: string, lines: MirrorLine[], accounts: MirrorAccount[]): MirrorIssue[] {
  const issues: MirrorIssue[] = []
  if (description.trim() === '') issues.push({ code: 'request.invalid.description', line: null })
  if (lines.length < 2) issues.push({ code: 'request.invalid.lines', line: null })
  let balance = 0n
  lines.forEach((line, index) => {
    const account = accounts.find((candidate) => candidate.id === line.accountId)
    if (!account) {
      issues.push({ code: 'currency.account_required', line: index })
      return
    }
    if (!/^[A-Z]{3}$/i.test(line.currency)) issues.push({ code: 'currency.invalid', line: index })
    if (!currencyPolicyAllows(account, line.currency)) issues.push({ code: 'currency_policy.currency_not_allowed', line: index })
    balance += BigInt(line.amountMinor)
  })
  if (balance !== 0n) issues.push({ code: 'entry.unbalanced', line: null })
  return issues
}