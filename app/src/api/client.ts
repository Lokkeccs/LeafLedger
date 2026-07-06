import createClient from 'openapi-fetch'
import type { paths } from './schema'

// Same-origin by default. Real base-URL selection and auth-header wiring is the
// P3 app-shell concern (Non-goal for P1-WP04); the client here is real + typed.
const baseUrl = '/'

/** The single, typed HTTP client generated from the OpenAPI contract. */
export const apiClient = createClient<paths>({ baseUrl })

export type ApiClient = typeof apiClient
