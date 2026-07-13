# TanStack Query conventions

`queryClient` is the shared application client. Queries stay fresh for 30 seconds, are retained for five minutes, do not refetch just because focus changed, and retry at most twice. Client errors (HTTP 4xx) are not retried; network and server failures may retry. Query and mutation failures throw so the owning route boundary can present the failure without exposing a stack trace.

All query keys come from `qk` in `queryKeys.ts`. Keys are hierarchical and module-namespaced so invalidation can target a precise resource or a whole module. Mutations invalidate through this factory, never through hand-written raw arrays. The report and feature-specific factories are placeholders until their work packages add the corresponding application hooks.