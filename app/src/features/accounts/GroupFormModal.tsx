import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AccountManagementError } from '../../application/accounts'
import type { AccountGroup, GroupCommand, GroupUpdate } from '../../application/accountGroups'
import { FormField, FormSection, ModalShell } from '../../shared'

type GroupFormModalProps = {
  group?: AccountGroup
  open: boolean
  submitting: boolean
  error?: { issues: Array<{ code: string; message: string; field?: string }> } | null
  onClose: () => void
  onSubmit: (values: GroupCommand | GroupUpdate) => Promise<void>
}

export function GroupFormModal({ group, open, submitting, error, onClose, onSubmit }: GroupFormModalProps) {
  const { t } = useTranslation()
  const [name, setName] = useState(group?.name ?? '')
  const [rangeStart, setRangeStart] = useState(group?.rangeStart.toString() ?? '')
  const [rangeEnd, setRangeEnd] = useState(group?.rangeEnd.toString() ?? '')
  const [fxPolicy, setFxPolicy] = useState(group?.fxPolicy ?? '')
  const [clientError, setClientError] = useState('')
  const issueFor = (field: string) => error?.issues.find((issue) => issue.field === field)?.message
  const formError = clientError || error?.issues.find((issue) => !issue.field)?.message

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const start = Number(rangeStart); const end = Number(rangeEnd)
    if (!name.trim() || !Number.isInteger(start) || !Number.isInteger(end)) { setClientError(t('accountsPage.validation.required')); return }
    if (start > end) { setClientError(t('accountsPage.validation.rangeOrder')); return }
    setClientError('')
    try {
      await onSubmit({ name: name.trim(), rangeStart: start, rangeEnd: end, parentId: group?.parentId ?? null, fxPolicy: fxPolicy || null })
    } catch (error) {
      if (error instanceof AccountManagementError) return
      throw error
    }
  }

  return <ModalShell open={open} {...(submitting ? {} : { onClose })} closeLabel={t('common.close')} title={group ? t('accountsPage.editGroup') : t('accountsPage.newGroup')}>
    <form id="group-form" onSubmit={(event) => void submit(event)}>
      <FormSection title={t('accountsPage.sections.group')} style={{ display: 'grid', gap: 'var(--space-3)' }}>
        <FormField id="group-name" label={t('accountsPage.fields.name')} error={issueFor('name')} value={name} onChange={(event) => setName(event.target.value)} />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-3)' }}>
          <FormField id="group-range-start" label={t('accountsPage.fields.rangeStart')} error={issueFor('rangeStart')} value={rangeStart} onChange={(event) => setRangeStart(event.target.value)} inputMode="numeric" />
          <FormField id="group-range-end" label={t('accountsPage.fields.rangeEnd')} error={issueFor('rangeEnd')} value={rangeEnd} onChange={(event) => setRangeEnd(event.target.value)} inputMode="numeric" />
        </div>
        <FormField id="group-fx-policy" label={t('accountsPage.fields.fxPolicy')} value={fxPolicy} onChange={(event) => setFxPolicy(event.target.value)} />
      </FormSection>
      {formError && <p role="alert" style={{ color: 'var(--color-danger)' }}>{formError}</p>}
    </form>
    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 'var(--space-2)' }}>
      <button type="button" onClick={onClose} disabled={submitting}>{t('common.cancel')}</button>
      <button type="submit" form="group-form" disabled={submitting}>{submitting ? t('common.saving') : t('common.save')}</button>
    </div>
  </ModalShell>
}