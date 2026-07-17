import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { DataTable, type DataColumn } from '../../shared/DataTable'
import { DateField } from '../../shared/DateField'
import { FormField } from '../../shared/FormField'
import { FormSection } from '../../shared/FormSection'
import { MoneyInput } from '../../shared/MoneyInput'
import { ModalShell } from '../../shared/ModalShell'
import { ToggleSwitch } from '../../shared/ToggleSwitch'

type SampleRow = { label: string; amount: string }
const rows: SampleRow[] = [{ label: 'designGallery.sampleRow', amount: 'designGallery.sampleAmount' }]
const columns: DataColumn<SampleRow>[] = [
  { header: 'designGallery.label', render: (row) => row.label },
  { header: 'designGallery.amount', render: (row) => row.amount, align: 'right' },
]

export function DesignSystemPage() {
  const { t } = useTranslation()
  const [modalOpen, setModalOpen] = useState(false)
  const translatedRows = rows.map((row) => ({ ...row, label: t(row.label), amount: t(row.amount) }))
  const translatedColumns = columns.map((column) => ({ ...column, header: t(column.header), render: (row: SampleRow) => column.header === 'designGallery.label' ? row.label : row.amount }))
  return <div className="design-gallery">
    <p className="eyebrow">{t('nav.design')}</p>
    <h1>{t('nav.design')}</h1>
    <section className="design-section"><h2>{t('designGallery.colors')}</h2><div className="token-swatches">{['primary', 'surface', 'surface-raised', 'border', 'text', 'success', 'warning', 'danger', 'info'].map((token) => <div className="token-swatch" key={token}><span style={{ background: `var(--color-${token})` }} />{t(`designGallery.tokens.${token}`)}</div>)}</div><div className="theme-previews"><div className="theme-preview theme-preview-light"><strong>{t('designGallery.light')}</strong><span>{t('designGallery.previewSurface')}</span></div><div className="theme-preview theme-preview-dark"><strong>{t('designGallery.dark')}</strong><span>{t('designGallery.previewSurface')}</span></div></div></section>
    <section className="design-section"><h2>{t('designGallery.type')}</h2><p className="type-xl">{t('designGallery.typeExample')}</p><p className="type-base">{t('designGallery.typeDescription')}</p><p className="type-mono">CHF 1,240.00</p></section>
    <section className="design-section"><h2>{t('designGallery.spacing')}</h2><div className="spacing-samples">{['1', '2', '3', '4', '5', '6'].map((space) => <span key={space} style={{ width: `var(--space-${space})`, height: 'var(--space-4)' }} />)}</div><div className="shape-samples"><span className="shape-sample shape-sm" /><span className="shape-sample shape-md" /><span className="shape-sample shape-lg" /><span className="shape-sample shape-shadow" /></div></section>
    <section className="design-section"><h2>{t('designGallery.primitives')}</h2><FormSection title={t('designGallery.formSection')}><FormField id="gallery-name" label={t('common.name')} placeholder={t('designGallery.example')} /><DateField id="gallery-date" label={t('designGallery.date')} value="2026-01-01" onChange={() => undefined} /><MoneyInput id="gallery-money" label={t('designGallery.amount')} currency="CHF" value={124000} onChange={() => undefined} /><ToggleSwitch label={t('common.active')} checked onChange={() => undefined} /></FormSection><DataTable data={translatedRows} rows={translatedRows} columns={translatedColumns} rowKey={(row) => row.label} emptyState={null} noMatchState={null} ariaLabel={t('designGallery.primitives')} /><button type="button" onClick={() => setModalOpen(true)}>{t('designGallery.openModal')}</button><ModalShell open={modalOpen} onClose={() => setModalOpen(false)} closeLabel={t('common.close')} title={t('designGallery.modalTitle')} footer={<button type="button" onClick={() => setModalOpen(false)}>{t('common.close')}</button>}>{t('designGallery.modalBody')}</ModalShell></section>
  </div>
}