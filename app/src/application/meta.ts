import { apiClient, type ApiClient } from '../api/client'
import type { components } from '../api/schema'

/** API metadata as consumed by the app. */
export type Meta = components['schemas']['MetaResponse']

/**
 * Fetches API metadata through the generated client.
 *
 * `client` is injectable for tests; it defaults to the shared, typed api
 * client. This is the application layer's single point of access to `src/api`
 * (features → application → api).
 */
export async function getMeta(client: ApiClient = apiClient): Promise<Meta> {
  const { data } = await client.GET('/api/v1/meta')
  if (data === undefined) {
    throw new Error('Failed to fetch API metadata')
  }
  return data
}

/**
 * Fetches API metadata through the generated client.
 *
 * `client` is injectable for tests; it defaults to the shared, typed api
 * client. This is the application layer's single point of access to `src/api`
 * (features → application → api).
 */
export async function getMeta(client: ApiClient = apiClient): Promise<Meta> {
  const { data } = await client.GET('/api/v1/meta')
  if (data === undefined) {
    throw new Error('Failed to fetch API metadata')
  }
  return { name: data.name, version: data.version }
}
