import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AccountManagementError, type Account, type AccountCommand, type AccountUpdate } from '../../application/accounts'
import type { AccountGroup } from '../../application/accountGroups'
import { FormField, FormSection, ModalShell } from '../../shared'

const accountKinds = ['Asset', 'Liability', 'Equity', 'Income', 'Expense'] as const

type FormValues = {
  groupId: string
  code: string
  name: string
  currency: string
  kind: string
  validFrom: string
  validTo: string
  fxPolicy: string
}

type AccountFormModalProps = {
  account?: Account
  groups: AccountGroup[]
  open: boolean
  submitting: boolean
  error?: { issues: Array<{ code: string; message: string; field?: string }> } | null
  onClose: () => void
  onSubmit: (values: AccountCommand | AccountUpdate) => Promise<void>
}

function initialValues(account?: Account): FormValues {
  return {
    groupId: account?.groupId ?? '', code: account?.code.toString() ?? '', name: account?.name ?? '',
    currency: account?.currency ?? 'CHF', kind: account?.kind ?? 'Asset', validFrom: account?.validFrom ?? '',
    validTo: account?.validTo ?? '', fxPolicy: account?.fxPolicy ?? '',
  }
}

export function AccountFormModal({ account, groups, open, submitting, error, onClose, onSubmit }: AccountFormModalProps) {
  const { t } = useTranslation()
  const [values, setValues] = useState(() => initialValues(account))
  const [clientError, setClientError] = useState('')

  const update = (field: keyof FormValues, value: string) => setValues((current) => ({ ...current, [field]: value }))
  const issueFor = (field: string) => error?.issues.find((issue) => issue.field === field)?.message
  const formError = clientError || error?.issues.find((issue) => !issue.field)?.message

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const code = Number(values.code)
    if (!values.name.trim() || !values.groupId || !Number.isInteger(code)) { setClientError(t('accountsPage.validation.required')); return }
    if (values.validFrom && values.validTo && values.validFrom > values.validTo) { setClientError(t('accountsPage.validation.dateOrder')); return }
    setClientError('')
    try {
      await onSubmit({
        groupId: values.groupId, code, name: values.name.trim(), currency: values.currency.trim().toUpperCase(), kind: values.kind,
        ...(account ? {} : { isActive: true }), validFrom: values.validFrom || null, validTo: values.validTo || null, fxPolicy: values.fxPolicy || null,
      })
    } catch (error) {
      if (error instanceof AccountManagementError) return
      throw error
    }
  }

  return <ModalShell open={open} {...(submitting ? {} : { onClose })} closeLabel={t('common.close')} title={account ? t('accountsPage.editAccount') : t('accountsPage.newAccount')}>
    <form id="account-form" onSubmit={(event) => void submit(event)}>
      <FormSection title={t('accountsPage.sections.details')} style={{ display: 'grid', gap: 'var(--space-3)' }}>
        <FormField id="account-group" label={t('accountsPage.fields.group')} error={issueFor('groupId')} control={<select value={values.groupId} onChange={(event) => update('groupId', event.target.value)}><option value="">{t('accountsPage.fields.selectGroup')}</option>{groups.map((group) => <option key={group.id} value={group.id}>{group.name} ({group.rangeStart}-{group.rangeEnd})</option>)}</select>} />
        <FormField id="account-code" label={t('accountsPage.fields.code')} error={issueFor('code')} value={values.code} onChange={(event) => update('code', event.target.value)} inputMode="numeric" />
        <FormField id="account-name" label={t('accountsPage.fields.name')} error={issueFor('name')} value={values.name} onChange={(event) => update('name', event.target.value)} />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-3)' }}>
          <FormField id="account-currency" label={t('accountsPage.fields.currency')} error={issueFor('currency')} value={values.currency} onChange={(event) => update('currency', event.target.value)} maxLength={3} />
          <FormField id="account-kind" label={t('accountsPage.fields.kind')} error={issueFor('kind')} control={<select value={values.kind} onChange={(event) => update('kind', event.target.value)}>{accountKinds.map((kind) => <option key={kind} value={kind}>{t(`accountsPage.kinds.${kind}`)}</option>)}</select>} />
        </div>
      </FormSection>
      <FormSection title={t('accountsPage.sections.validity')} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-3)', marginTop: 'var(--space-3)' }}>
        <FormField id="account-valid-from" label={t('accountsPage.fields.validFrom')} type="date" value={values.validFrom} onChange={(event) => update('validFrom', event.target.value)} />
        <FormField id="account-valid-to" label={t('accountsPage.fields.validTo')} type="date" value={values.validTo} onChange={(event) => update('validTo', event.target.value)} />
        <FormField id="account-fx-policy" label={t('accountsPage.fields.fxPolicy')} value={values.fxPolicy} onChange={(event) => update('fxPolicy', event.target.value)} />
      </FormSection>
      {formError && <p role="alert" style={{ color: 'var(--color-danger)' }}>{formError}</p>}
    </form>
    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 'var(--space-2)' }}>
      <button type="button" onClick={onClose} disabled={submitting}>{t('common.cancel')}</button>
      <button type="submit" form="account-form" disabled={submitting}>{submitting ? t('common.saving') : t('common.save')}</button>
    </div>
  </ModalShell>
}