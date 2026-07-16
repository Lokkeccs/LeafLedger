import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { acquireApiToken } from '../auth/authTokens'
import { useAuth } from '../auth/useAuth'
import { queryKeysForTopic } from './invalidationMap'

const batchWindowMs = 50
type InvalidationPayload = { spaceId: string; topic: string }

export function useSpaceInvalidation(spaceId: string): void {
  const { account } = useAuth()
  const queryClient = useQueryClient()
  const pendingKeys = useRef(new Map<string, readonly unknown[]>());
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  useEffect(() => {
    if (!account || !spaceId) return
    const pendingKeysForConnection = pendingKeys.current

    const flush = () => {
      timer.current = undefined
      const keys = [...pendingKeysForConnection.values()]
      pendingKeysForConnection.clear()
      void Promise.all(keys.map((queryKey) => queryClient.invalidateQueries({ queryKey })))
    }

    const queueInvalidation = (payload: InvalidationPayload) => {
      if (payload.spaceId !== spaceId) return
      const keys = queryKeysForTopic(payload.topic, spaceId)
      if (keys.length === 0) {
        console.warn(`Ignoring unknown space invalidation topic: ${payload.topic}`)
        return
      }
      for (const queryKey of keys) pendingKeysForConnection.set(JSON.stringify(queryKey), queryKey)
      if (timer.current === undefined) timer.current = setTimeout(flush, batchWindowMs)
    }

    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`/hubs/space?spaceId=${encodeURIComponent(spaceId)}`, {
        accessTokenFactory: async () => (await acquireApiToken(account)) ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('spaceInvalidated', queueInvalidation)
    void connection.start()

    return () => {
      connection.off('spaceInvalidated', queueInvalidation)
      if (timer.current !== undefined) clearTimeout(timer.current)
      timer.current = undefined
      pendingKeysForConnection.clear()
      void connection.stop()
    }
  }, [account, queryClient, spaceId])
}