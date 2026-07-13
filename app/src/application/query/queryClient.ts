import { QueryClient } from '@tanstack/react-query'

const serverErrorStatus = (error: unknown): number | undefined => {
  if (typeof error !== 'object' || error === null || !('status' in error)) return undefined
  const status = error.status
  return typeof status === 'number' ? status : undefined
}

/** Creates the shared client so tests and future shells can own their lifecycle. */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        retry: (failureCount, error) => {
          const status = serverErrorStatus(error)
          if (status !== undefined && status >= 400 && status < 500) return false
          return failureCount < 2
        },
        refetchOnWindowFocus: false,
        throwOnError: true,
      },
      mutations: { retry: false, throwOnError: true },
    },
  })
}

export const queryClient = createQueryClient()