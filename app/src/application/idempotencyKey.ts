const crockfordAlphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'

function encode(value: bigint, length: number): string {
  let result = ''
  for (let index = 0; index < length; index += 1) {
    result = crockfordAlphabet[Number(value & 31n)] + result
    value >>= 5n
  }
  return result
}

/** Creates the 26-character ULID accepted by the API idempotency boundary. */
export function newIdempotencyKey(): string {
  const random = new Uint8Array(10)
  globalThis.crypto.getRandomValues(random)
  let randomValue = 0n
  for (const byte of random) randomValue = (randomValue << 8n) | BigInt(byte)
  return `${encode(BigInt(Date.now()), 10)}${encode(randomValue, 16)}`
}