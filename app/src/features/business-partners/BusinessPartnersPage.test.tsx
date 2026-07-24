// @vitest-environment jsdom
import { QueryClientProvider } from '@tanstack/react-query'
import { I18nextProvider } from 'react-i18next'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BusinessPartnerManagementError } from '../../application/businessPartners'
import { createQueryClient } from '../../application/query/queryClient'
import { i18n } from '../../i18n'
import { BusinessPartnersPage } from './BusinessPartnersPage'

const { useBusinessPartners, useCreateBusinessPartner, useUpdateBusinessPartner, useDeleteBusinessPartner } = vi.hoisted(() => ({
  useBusinessPartners: vi.fn(),
  useCreateBusinessPartner: vi.fn(),
  useUpdateBusinessPartner: vi.fn(),
  useDeleteBusinessPartner: vi.fn(),
}))
vi.mock('../../application/query/useBusinessPartners', () => ({ useBusinessPartners }))
vi.mock('../../application/query/useCreateBusinessPartner', () => ({ useCreateBusinessPartner }))
vi.mock('../../application/query/useUpdateBusinessPartner', () => ({ useUpdateBusinessPartner }))
vi.mock('../../application/query/useDeleteBusinessPartner', () => ({ useDeleteBusinessPartner }))

const partner = {
  id: 'bp-1', name: 'Acme', partnerNumber: 'P-1', type: 'customer', countryCode: 'CH',
  isActive: true, validFrom: null, validTo: null, notes: null, version: '7',
}

function renderPage() {
  return render(<I18nextProvider i18n={i18n}><MemoryRouter><QueryClientProvider client={createQueryClient()}><BusinessPartnersPage /></QueryClientProvider></MemoryRouter></I18nextProvider>)
}

function idleMutation() {
  return { isPending: false, error: null, mutate: vi.fn(), mutateAsync: vi.fn() }
}

describe('BusinessPartnersPage', () => {
  beforeEach(() => {
    useBusinessPartners.mockReset()
    useCreateBusinessPartner.mockReset()
    useUpdateBusinessPartner.mockReset()
    useDeleteBusinessPartner.mockReset()
    useBusinessPartners.mockReturnValue({ isPending: false, isError: false, data: [partner] })
    useCreateBusinessPartner.mockReturnValue(idleMutation())
    useUpdateBusinessPartner.mockReturnValue(idleMutation())
    useDeleteBusinessPartner.mockReturnValue(idleMutation())
  })

  it('renders a friendly permission message for a server 403', () => {
    useCreateBusinessPartner.mockReturnValue({ ...idleMutation(), error: new BusinessPartnerManagementError(403, [{ code: 'forbidden', message: 'Forbidden' }]) })
    renderPage()
    expect(screen.getByRole('alert').textContent).toBe('You do not have permission to manage business partners in this space.')
  })

  it('renders a friendly in-use message for a protected delete', () => {
    useDeleteBusinessPartner.mockReturnValue({ ...idleMutation(), error: new BusinessPartnerManagementError(409, [{ code: 'partner.in_use', message: 'In use' }]) })
    renderPage()
    expect(screen.getByRole('alert').textContent).toBe('This partner is in use and cannot be deleted.')
  })
})
