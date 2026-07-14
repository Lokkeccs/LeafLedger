import { useTranslation } from 'react-i18next'
import type { Account } from '../../application/accounts'

type AccountPickerProps = { accounts: Account[]; value: string; onChange: (accountId: string, currency: string) => void; id: string; error?: string | undefined }

export function AccountPicker({ accounts, value, onChange, id, error }: AccountPickerProps) {
  const { t } = useTranslation()
  return <select id={id} value={value} aria-invalid={error ? true : undefined} aria-label={t('journalEntry.account')} onChange={(event) => { const account = accounts.find((item) => item.id === event.target.value); if (account) onChange(account.id, account.currency) }}>
    <option value="">{t('journalEntry.selectAccount')}</option>
    {accounts.map((account) => <option key={account.id} value={account.id}>{account.code} — {account.name}</option>)}
  </select>
}