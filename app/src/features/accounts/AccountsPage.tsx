import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { AccountManagementError, createAccountSubmission, createAccountUpdateSubmission, type Account } from '../../application/accounts'
import { createGroupSubmission, createGroupUpdateSubmission, type AccountGroup } from '../../application/accountGroups'
import { useAccountGroups } from '../../application/query/useAccountGroups'
import { useAccounts } from '../../application/query/useAccounts'
import { useCreateAccount } from '../../application/query/useCreateAccount'
import { useCreateAccountGroup } from '../../application/query/useCreateAccountGroup'
import { useSetAccountActive } from '../../application/query/useSetAccountActive'
import { useUpdateAccount } from '../../application/query/useUpdateAccount'
import { useUpdateAccountGroup } from '../../application/query/useUpdateAccountGroup'
import { useImportAccounts } from '../../application/query/useImportAccounts'
import { useImportGroups } from '../../application/query/useImportGroups'
import { exportAccountsCsv, exportGroupsCsv } from '../../application/accountImport'
import { DataTable, type ColumnDef } from '../../shared'
import { AccountFormModal } from './AccountFormModal'
import { GroupFormModal } from './GroupFormModal'
import { ImportModal } from './ImportModal'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1'

export function AccountsPage() {
  const { t } = useTranslation()
  const accountsQuery = useAccounts(demoSpaceId)
  const groupsQuery = useAccountGroups(demoSpaceId)
  const createAccountMutation = useCreateAccount(demoSpaceId)
  const updateAccountMutation = useUpdateAccount(demoSpaceId)
  const setActiveMutation = useSetAccountActive(demoSpaceId)
  const createGroupMutation = useCreateAccountGroup(demoSpaceId)
  const updateGroupMutation = useUpdateAccountGroup(demoSpaceId)
  const importAccountsMutation = useImportAccounts(demoSpaceId)
  const importGroupsMutation = useImportGroups(demoSpaceId)
  const [accountModal, setAccountModal] = useState<Account | 'new' | null>(null)
  const [groupModal, setGroupModal] = useState<AccountGroup | 'new' | null>(null)
  const [importOpen, setImportOpen] = useState(false)
  const [exportError, setExportError] = useState<AccountManagementError | null>(null)

  if (accountsQuery.isPending || groupsQuery.isPending) return <p role="status">{t('accountsPage.loading')}</p>
  if (accountsQuery.isError) throw accountsQuery.error
  if (groupsQuery.isError) throw groupsQuery.error

  const accounts = accountsQuery.data
  const groups = groupsQuery.data
  const mutationError = [createAccountMutation.error, updateAccountMutation.error, setActiveMutation.error, createGroupMutation.error, updateGroupMutation.error, importAccountsMutation.error, importGroupsMutation.error].find(Boolean)
  const managementError = mutationError instanceof AccountManagementError ? mutationError : undefined
  const errorFor = (error: unknown) => error instanceof AccountManagementError ? error : null
  const busy = createAccountMutation.isPending || updateAccountMutation.isPending || setActiveMutation.isPending || createGroupMutation.isPending || updateGroupMutation.isPending || importAccountsMutation.isPending || importGroupsMutation.isPending
  const download = (name: string, content: string) => { const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' })); const link = document.createElement('a'); link.href = url; link.download = name; link.click(); URL.revokeObjectURL(url) }
  const exportCatalog = async (kind: 'accounts' | 'groups') => {
    setExportError(null)
    try {
      const csv = kind === 'accounts' ? await exportAccountsCsv(demoSpaceId) : await exportGroupsCsv(demoSpaceId)
      download(kind === 'accounts' ? 'accounts.csv' : 'account-groups.csv', csv)
    } catch (error) {
      if (error instanceof AccountManagementError) setExportError(error)
      else throw error
    }
  }
  const columns: ColumnDef<Account>[] = [
    { header: t('accountsPage.columns.code'), render: (account) => account.code, width: 100 },
    { header: t('accountsPage.columns.name'), render: (account) => <Link to={`/reports/account/${account.id}`}>{account.name}</Link>, width: 280 },
    { header: t('accountsPage.columns.currency'), render: (account) => account.currency, width: 120 },
    { header: t('accountsPage.columns.kind'), render: (account) => account.kind, width: 160 },
    { header: t('accountsPage.columns.active'), render: (account) => account.isActive ? t('common.active') : t('common.inactive'), width: 120 },
    { type: 'actions', render: (account) => <><button type="button" onClick={(event) => { event.stopPropagation(); setAccountModal(account) }}>{t('accountsPage.edit')}</button><button type="button" onClick={(event) => { event.stopPropagation(); setActiveMutation.mutate({ accountId: account.id, active: !account.isActive }) }} disabled={busy}>{account.isActive ? t('accountsPage.deactivate') : t('accountsPage.activate')}</button></> },
  ]
  const emptyState = <p role="status">{t('accountsPage.empty')}</p>
  const groupColumns: ColumnDef<AccountGroup>[] = [
    { header: t('accountsPage.groupColumns.name'), render: (group) => group.name, width: 280 },
    { header: t('accountsPage.groupColumns.range'), render: (group) => `${group.rangeStart}–${group.rangeEnd}`, width: 180 },
    { header: t('accountsPage.groupColumns.fxPolicy'), render: (group) => group.fxPolicy ?? t('common.no'), width: 180 },
    { type: 'actions', render: (group) => <button type="button" onClick={() => setGroupModal(group)}>{t('accountsPage.edit')}</button> },
  ]

  const accountModalError = errorFor(createAccountMutation.error ?? updateAccountMutation.error)
  const groupModalError = errorFor(createGroupMutation.error ?? updateGroupMutation.error)
  const submitAccount = async (input: Parameters<typeof createAccountSubmission>[0] | Parameters<typeof createAccountUpdateSubmission>[0]) => {
    if (accountModal && accountModal !== 'new') await updateAccountMutation.mutateAsync({ accountId: accountModal.id, submission: createAccountUpdateSubmission(input) })
    else await createAccountMutation.mutateAsync(createAccountSubmission(input as Parameters<typeof createAccountSubmission>[0]))
    setAccountModal(null)
  }
  const submitGroup = async (input: Parameters<typeof createGroupSubmission>[0] | Parameters<typeof createGroupUpdateSubmission>[0]) => {
    if (groupModal && groupModal !== 'new') await updateGroupMutation.mutateAsync({ groupId: groupModal.id, submission: createGroupUpdateSubmission(input) })
    else await createGroupMutation.mutateAsync(createGroupSubmission(input as Parameters<typeof createGroupSubmission>[0]))
    setGroupModal(null)
  }

  return <section className="accounts-page">
    <p className="eyebrow">{t('accountsPage.eyebrow')}</p>
    <h1>{t('accountsPage.title')}</h1>
    <p className="lead">{t('accountsPage.description')}</p>
    <div className="accounts-toolbar"><button type="button" onClick={() => setAccountModal('new')}>{t('accountsPage.newAccount')}</button><button type="button" onClick={() => setGroupModal('new')}>{t('accountsPage.newGroup')}</button><button type="button" onClick={() => setImportOpen(true)}>{t('accountsPage.import.open')}</button><button type="button" onClick={() => void exportCatalog('accounts')} disabled={busy}>{t('accountsPage.export.accounts')}</button><button type="button" onClick={() => void exportCatalog('groups')} disabled={busy}>{t('accountsPage.export.groups')}</button></div>
    {(managementError?.status === 403 || exportError?.status === 403) && <p role="alert">{t('accountsPage.permissionDenied')}</p>}
    {managementError && managementError.status !== 403 && <p role="alert">{t('accountsPage.serverError')}</p>}
    {exportError && exportError.status !== 403 && <p role="alert">{t('accountsPage.serverError')}</p>}
    <DataTable data={accounts} rows={accounts} columns={columns} rowKey={(account) => account.id} emptyState={emptyState} noMatchState={emptyState} ariaLabel={t('accountsPage.tableLabel')} />
    <h2>{t('accountsPage.groupsTitle')}</h2>
    <DataTable data={groups} rows={groups} columns={groupColumns} rowKey={(group) => group.id} emptyState={<p role="status">{t('accountsPage.empty')}</p>} noMatchState={<p role="status">{t('accountsPage.empty')}</p>} ariaLabel={t('accountsPage.groupsTableLabel')} />
    <AccountFormModal key={accountModal && accountModal !== 'new' ? accountModal.id : 'new'} {...(accountModal && accountModal !== 'new' ? { account: accountModal } : {})} groups={groups} open={accountModal !== null} submitting={createAccountMutation.isPending || updateAccountMutation.isPending} error={accountModalError} onClose={() => setAccountModal(null)} onSubmit={submitAccount} />
    <GroupFormModal key={groupModal && groupModal !== 'new' ? groupModal.id : 'new'} {...(groupModal && groupModal !== 'new' ? { group: groupModal } : {})} open={groupModal !== null} submitting={createGroupMutation.isPending || updateGroupMutation.isPending} error={groupModalError} onClose={() => setGroupModal(null)} onSubmit={submitGroup} />
    <ImportModal open={importOpen} submitting={busy} report={importAccountsMutation.data ?? importGroupsMutation.data} error={importAccountsMutation.error ?? importGroupsMutation.error} onClose={() => { setImportOpen(false); importAccountsMutation.reset(); importGroupsMutation.reset() }} onSubmit={async (kind, csv) => { if (kind === 'accounts') await importAccountsMutation.mutateAsync(csv); else await importGroupsMutation.mutateAsync(csv) }} />
  </section>
}