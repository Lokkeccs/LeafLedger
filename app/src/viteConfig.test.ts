import { describe, expect, it } from 'vitest'
import config from '../vite.config'

type ProxyEntry = string | { target?: string; ws?: boolean }

function proxyEntry(proxy: unknown, path: string): ProxyEntry | undefined {
  if (!proxy || Array.isArray(proxy) || typeof proxy !== 'object') return undefined
  return (proxy as Record<string, ProxyEntry>)[path]
}

describe('Vite local API routing', () => {
  it.each(['server', 'preview'] as const)('proxies SignalR hubs in %s mode', (mode) => {
    const modeConfig = config[mode]
    const hubProxy = proxyEntry(modeConfig?.proxy, '/hubs')

    expect(hubProxy).toEqual({ target: 'http://localhost:8080', ws: true })
  })
})