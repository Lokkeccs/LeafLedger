import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BusinessPartnerManagementError, createBusinessPartnerSubmission, createBusinessPartnerUpdateSubmission, type BusinessPartner } from '../../application/businessPartners'
import { useBusinessPartners } from '../../application/query/useBusinessPartners'
import { useCreateBusinessPartner } from '../../application/query/useCreateBusinessPartner'
import { useDeleteBusinessPartner } from '../../application/query/useDeleteBusinessPartner'
import { useUpdateBusinessPartner } from '../../application/query/useUpdateBusinessPartner'
import { DataTable, type ColumnDef } from '../../shared'
import { BusinessPartnerFormModal } from './BusinessPartnerFormModal'

const demoSpaceId = import.meta.env.VITE_DEMO_SPACE_ID || '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1'

export function BusinessPartnersPage() {
  const { t } = useTranslation()
  const query = useBusinessPartners(demoSpaceId)
  const createMutation = useCreateBusinessPartner(demoSpaceId)
  const updateMutation = useUpdateBusinessPartner(demoSpaceId)
  const deleteMutation = useDeleteBusinessPartner(demoSpaceId)
  const [modal, setModal] = useState<BusinessPartner | 'new' | null>(null)
  if (query.isPending) return <p role="status">{t('businessPartnersPage.loading')}</p>
  if (query.isError) throw query.error

  const mutationError = [createMutation.error, updateMutation.error, deleteMutation.error].find(Boolean)
  const managementError = mutationError instanceof BusinessPartnerManagementError ? mutationError : null
  const busy = createMutation.isPending || updateMutation.isPending || deleteMutation.isPending
  const submit = async (input: Parameters<typeof createBusinessPartnerSubmission>[0] | Parameters<typeof createBusinessPartnerUpdateSubmission>[0]) => {
    if (modal && modal !== 'new') await updateMutation.mutateAsync({ partnerId: modal.id, submission: createBusinessPartnerUpdateSubmission(input as Parameters<typeof createBusinessPartnerUpdateSubmission>[0]) })
    else await createMutation.mutateAsync(createBusinessPartnerSubmission(input as Parameters<typeof createBusinessPartnerSubmission>[0]))
    setModal(null)
  }
  const toggleActive = (partner: BusinessPartner) => void updateMutation.mutateAsync({ partnerId: partner.id, submission: createBusinessPartnerUpdateSubmission({ name: partner.name, partnerNumber: partner.partnerNumber, type: partner.type, countryCode: partner.countryCode, validFrom: partner.validFrom, validTo: partner.validTo, notes: partner.notes, isActive: !partner.isActive, version: partner.version }) })
  const remove = (partner: BusinessPartner) => { if (window.confirm(t('businessPartnersPage.confirmDelete', { name: partner.name }))) void deleteMutation.mutate({ partnerId: partner.id }) }
  const columns: ColumnDef<BusinessPartner>[] = [
    { header: t('businessPartnersPage.columns.name'), render: (partner) => partner.name, width: 260 },
    { header: t('businessPartnersPage.columns.number'), render: (partner) => partner.partnerNumber ?? t('common.no'), width: 150 },
    { header: t('businessPartnersPage.columns.type'), render: (partner) => t(`businessPartnersPage.types.${partner.type}`, { defaultValue: partner.type }), width: 170 },
    { header: t('businessPartnersPage.columns.country'), render: (partner) => partner.countryCode ?? t('common.no'), width: 110 },
    { header: t('businessPartnersPage.columns.active'), render: (partner) => partner.isActive ? t('common.active') : t('common.inactive'), width: 110 },
    { type: 'actions', render: (partner) => <><button type="button" onClick={() => setModal(partner)} disabled={busy}>{t('common.edit')}</button><button type="button" onClick={() => toggleActive(partner)} disabled={busy}>{partner.isActive ? t('businessPartnersPage.deactivate') : t('businessPartnersPage.activate')}</button><button type="button" onClick={() => remove(partner)} disabled={busy}>{t('common.delete')}</button></> },
  ]
  const errorMessage = managementError?.status === 403 ? t('businessPartnersPage.permissionDenied') : managementError?.issues.some((issue) => issue.code === 'partner.in_use') ? t('businessPartnersPage.inUse') : managementError?.status === 409 ? t('businessPartnersPage.versionConflict') : managementError ? t('businessPartnersPage.serverError') : ''
  return <section className="business-partners-page">
    <p className="eyebrow">{t('businessPartnersPage.eyebrow')}</p>
    <h1>{t('businessPartnersPage.title')}</h1>
    <p className="lead">{t('businessPartnersPage.description')}</p>
    <div className="accounts-toolbar"><button type="button" onClick={() => setModal('new')}>{t('businessPartnersPage.newPartner')}</button></div>
    {errorMessage && <p role="alert">{errorMessage}</p>}
    <DataTable data={query.data} rows={query.data} columns={columns} rowKey={(partner) => partner.id} emptyState={<p role="status">{t('businessPartnersPage.empty')}</p>} noMatchState={<p role="status">{t('businessPartnersPage.empty')}</p>} ariaLabel={t('businessPartnersPage.tableLabel')} />
    <BusinessPartnerFormModal key={modal && modal !== 'new' ? modal.id : 'new'} {...(modal && modal !== 'new' ? { partner: modal } : {})} open={modal !== null} submitting={createMutation.isPending || updateMutation.isPending} error={createMutation.error instanceof BusinessPartnerManagementError ? createMutation.error : updateMutation.error instanceof BusinessPartnerManagementError ? updateMutation.error : null} onClose={() => setModal(null)} onSubmit={submit} />
  </section>
}