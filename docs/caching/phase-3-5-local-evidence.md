# Phases 3–5 authenticated local evidence

Captured 2026-07-19 against one retained disposable Studio/project/media fixture.
The machine-readable report is `phase-3-5-local-evidence.json`. Identifiers,
credentials, keys, and payloads are omitted; response bodies are represented by
SHA-256 hashes.

Each group was measured with caching disabled, one cold cache-enabled batch, five
warm batches, and five batches through an API whose Redis endpoint was unreachable.
Every mode returned HTTP 200 and the same response hash for its group. Redis-unavailable
requests recorded error/circuit-bypass outcomes and returned authoritative data.

| Endpoint group | Disabled PostgreSQL commands/batch | Warm commands/batch | Reduction |
|---|---:|---:|---:|
| User profile and settings | 6.4 | 4.4 | 31% |
| Studio settings and references | 18.4 | 16.4 | 11% |
| Task milestones and labels | 8.4 | 6.4 | 24% |
| Notification unread count | 3.4 | 2.4 | 29% |
| Studio usage aggregate | 9.4 | 4.4 | 53% |
| Project list/detail/media | 28.4 | 11.4 | 60% |
| Media list/detail/storage | 16.4 | 10.4 | 37% |
| Album lists | 4.4 | 3.4 | 23% |
| Task lists | 4.4 | 3.4 | 23% |
| Timeline/list/editor bootstrap | 28.4 | 15.4 | 46% |

The warm namespace contained 30 keys using 16,160 bytes of measured Redis memory;
the instance reported zero evictions. Cold requests recorded misses and successful
fills, and every warm group recorded hits. Remaining PostgreSQL commands are expected
authorization, membership, generation, and current-revision projections; Redis never
decides authorization or optimistic concurrency.

Notification-page caching remains disabled because no polling evidence justified it.
Render-job caching remains disabled because no fallback polling fixture was required
for these endpoint groups. Rollback remains the global/business/domain flags; no
database flush is part of rollout or rollback.
