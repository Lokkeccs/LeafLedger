import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BusinessPartnerManagementError, type BusinessPartner, type BusinessPartnerCommand, type BusinessPartnerUpdate } from '../../application/businessPartners'
import { FormField, FormSection, ModalShell } from '../../shared'

type FormValues = {
  name: string
  partnerNumber: string
  type: string
  countryCode: string
  validFrom: string
  validTo: string
  notes: string
  isActive: boolean
}

type BusinessPartnerFormModalProps = {
  partner?: BusinessPartner
  open: boolean
  submitting: boolean
  error?: BusinessPartnerManagementError | null
  onClose: () => void
  onSubmit: (values: BusinessPartnerCommand | BusinessPartnerUpdate) => Promise<void>
}

const partnerTypes = ['customer', 'vendor', 'both', 'financial-services'] as const

function initialValues(partner?: BusinessPartner): FormValues {
  return {
    name: partner?.name ?? '', partnerNumber: partner?.partnerNumber ?? '', type: partner?.type ?? 'customer',
    countryCode: partner?.countryCode ?? '', validFrom: partner?.validFrom ?? '', validTo: partner?.validTo ?? '',
    notes: partner?.notes ?? '', isActive: partner?.isActive ?? true,
  }
}

export function BusinessPartnerFormModal({ partner, open, submitting, error, onClose, onSubmit }: BusinessPartnerFormModalProps) {
  const { t } = useTranslation()
  const [values, setValues] = useState(() => initialValues(partner))
  const [clientError, setClientError] = useState('')
  const update = (field: keyof FormValues, value: string | boolean) => setValues((current) => ({ ...current, [field]: value }))
  const issueFor = (field: string) => error?.issues.find((issue) => issue.field === field)?.message
  const formError = clientError || error?.issues.find((issue) => !issue.field)?.message

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!values.name.trim()) { setClientError(t('businessPartnersPage.validation.required')); return }
    if (values.validFrom && values.validTo && values.validFrom > values.validTo) { setClientError(t('businessPartnersPage.validation.dateOrder')); return }
    setClientError('')
    const input = {
      name: values.name.trim(), partnerNumber: values.partnerNumber.trim() || null, type: values.type,
      countryCode: values.countryCode.trim().toUpperCase() || null, validFrom: values.validFrom || null,
      validTo: values.validTo || null, notes: values.notes.trim() || null, isActive: values.isActive,
    }
    try {
      await onSubmit(partner ? { ...input, version: partner.version } : input)
    } catch (submissionError) {
      if (submissionError instanceof BusinessPartnerManagementError) return
      throw submissionError
    }
  }

  return <ModalShell open={open} {...(submitting ? {} : { onClose })} closeLabel={t('common.close')} title={partner ? t('businessPartnersPage.editPartner') : t('businessPartnersPage.newPartner')}>
    <form id="business-partner-form" onSubmit={(event) => void submit(event)}>
      <FormSection title={t('businessPartnersPage.sections.details')} style={{ display: 'grid', gap: 'var(--space-3)' }}>
        <FormField id="partner-name" label={t('businessPartnersPage.fields.name')} error={issueFor('name')} value={values.name} onChange={(event) => update('name', event.target.value)} />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-3)' }}>
          <FormField id="partner-number" label={t('businessPartnersPage.fields.partnerNumber')} error={issueFor('partnerNumber')} value={values.partnerNumber} onChange={(event) => update('partnerNumber', event.target.value)} />
          <FormField id="partner-type" label={t('businessPartnersPage.fields.type')} error={issueFor('type')} control={<select value={values.type} onChange={(event) => update('type', event.target.value)}>{partnerTypes.map((type) => <option key={type} value={type}>{t(`businessPartnersPage.types.${type}`)}</option>)}</select>} />
        </div>
        <FormField id="partner-country" label={t('businessPartnersPage.fields.countryCode')} error={issueFor('countryCode')} value={values.countryCode} onChange={(event) => update('countryCode', event.target.value)} maxLength={2} />
        <label htmlFor="partner-active"><input id="partner-active" type="checkbox" checked={values.isActive} onChange={(event) => update('isActive', event.target.checked)} /> {t('businessPartnersPage.fields.active')}</label>
      </FormSection>
      <FormSection title={t('businessPartnersPage.sections.validity')} style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-3)', marginTop: 'var(--space-3)' }}>
        <FormField id="partner-valid-from" label={t('businessPartnersPage.fields.validFrom')} error={issueFor('validFrom')} type="date" value={values.validFrom} onChange={(event) => update('validFrom', event.target.value)} />
        <FormField id="partner-valid-to" label={t('businessPartnersPage.fields.validTo')} error={issueFor('validTo')} type="date" value={values.validTo} onChange={(event) => update('validTo', event.target.value)} />
        <FormField id="partner-notes" label={t('businessPartnersPage.fields.notes')} error={issueFor('notes')} control={<textarea value={values.notes} onChange={(event) => update('notes', event.target.value)} rows={4} style={{ padding: 'var(--space-2) var(--space-3)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', background: 'var(--color-surface)', color: 'var(--color-text)', resize: 'vertical' }} />} />
      </FormSection>
      {error?.status === 409 && <p role="alert">{error.issues.some((issue) => issue.code === 'partner.version_conflict') ? t('businessPartnersPage.versionConflict') : formError}</p>}
      {error?.status !== 409 && formError && <p role="alert" style={{ color: 'var(--color-danger)' }}>{formError}</p>}
    </form>
    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 'var(--space-2)' }}>
      <button type="button" onClick={onClose} disabled={submitting}>{t('common.cancel')}</button>
      <button type="submit" form="business-partner-form" disabled={submitting}>{submitting ? t('common.saving') : t('common.save')}</button>
    </div>
  </ModalShell>
}