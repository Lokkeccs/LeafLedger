# TanStack Query conventions

Mutations follow the application boundary: the wrapper owns the generated-client call, the hook owns `useMutation`, and successful writes invalidate every affected key through `qk`. A submission carries one idempotency key so retries replay the same server operation.

`queryClient` is the shared application client. Queries stay fresh for 30 seconds, are retained for five minutes, do not refetch just because focus changed, and retry at most twice. Client errors (HTTP 4xx) are not retried; network and server failures may retry. Query and mutation failures throw so the owning route boundary can present the failure without exposing a stack trace.

All query keys come from `qk` in `queryKeys.ts`. Keys are hierarchical and module-namespaced so invalidation can target a precise resource or a whole module. Mutations invalidate through this factory, never through hand-written raw arrays. The report and feature-specific factories are placeholders until their work packages add the corresponding application hooks.

## Realtime invalidation map

The shell owns one authenticated SignalR connection. It receives data-free `spaceInvalidated` pings and batches refetches through this explicit map; features never subscribe to the hub directly.

| Module | Topic | Query keys |
| --- | --- | --- |
| Ledger posting | `journalEntries.list` | `qk.journalEntries.list(spaceId)` |
| Statement reporting | `reports.trialBalance` | `qk.reports.trialBalance(spaceId)`, `qk.reports.dashboard(spaceId)`, `qk.reports.balanceSheet(spaceId)`, `qk.reports.incomeStatement(spaceId)`, `['reports', 'accountLedger', spaceId]` |

This map covers the Phase-3 post and reverse mutations. Unknown topics are ignored and never cause broad cache invalidation.