// THROWAWAY — deliberate gate violation to prove pr.yml blocks (P1-WP01 criterion 4).
// This file must NOT be merged. It trips three enforced rules at once:
import { secret } from '../api/client' // no-restricted-imports: features must not import src/api

const unused = 42 // no-unused-vars

export async function ping() {
  const res = await fetch('/api/v1/ping') // no-restricted-globals: fetch banned outside src/api
  return { res, secret }
}
