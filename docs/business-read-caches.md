# Business read caches (Phases 3 through 5)

All business-read cache flags ship disabled. A domain is active only when all three gates are true:

1. `Caching__Enabled`
2. `Caching__BusinessReads__Enabled`
3. The domain flag, such as `Caching__Projects__Enabled`

`Caching__Notifications__Enabled` must remain false until notification polling and PostgreSQL command metrics show that page caching is justified. The unread-count cache has its own `Caching__NotificationCount__Enabled` flag.

## API Redis budget

The production `redis-api` container is capped at 128 MiB. Phase 5 adds an initial 8 MiB editor-document target, bringing the logical API-cache target to 72 MiB:

| Area | Target |
| --- | ---: |
| Phase 3 reference/settings | 8 MiB |
| Projects | 12 MiB |
| Media and albums | 24 MiB |
| Tasks | 12 MiB |
| Usage and notifications | 4 MiB |
| Editor documents and render status | 8 MiB |
| Growth headroom | 4 MiB |

The separate `redis-ai` container is capped at 384 MiB. ACLs restrict the API user to `kuvox:v1:api:*` and the AI user to `kuvox:v1:ai:*`.

## Correctness model

- PostgreSQL remains authoritative.
- Authorization and persisted Studio membership checks run before cache hits.
- Only successful, non-null, terminal DTOs are cached. Streams and signed/object downloads are never cached.
- List and page invalidation replaces a 128-bit generation under `kuvox:v1:api:gen:*`; old values expire naturally without scans.
- Mutation invalidation runs after a successful commit and fails open. Short TTLs are the final correctness bound.
- Keys include schema `1` plus the relevant viewer, owner, Studio, page, size, filter, `includeSystem`, scope/role, and generation identities.
- Timeline, timeline-list, image-composition, and render-status keys are viewer-neutral only because persisted authorization runs before their immutable revision/state lookup.
- Editor reads always query PostgreSQL for the current revision or render-state identity; Redis never stores a mutable “current revision” pointer.

## Rollout and evidence

Enable one domain at a time. For each endpoint group, capture redacted aggregates for caching disabled, cold, and warm requests:

- response status and body hash parity;
- PostgreSQL command count and latency;
- payload average, p95, and maximum;
- hit/miss/bypass/error totals and invalidations;
- key cardinality and Redis memory;
- endpoint p50 and p95.

Rollback is the domain flag. Do not flush either Redis instance; unreachable generations and values expire naturally.

The authenticated 2026-07-19 evidence is recorded in
`docs/caching/phase-3-5-local-evidence.md` and its machine-readable JSON companion.
Disabled, cold, warm, and Redis-unavailable hashes were equivalent for all ten
endpoint groups. Warm targeted PostgreSQL work fell for every group, and the measured
namespace used 30 keys/16,160 bytes with zero evictions.

Studio settings and task-reference prewarming is intentionally narrower than normal
read caching: startup requires explicit `Caching__PrewarmStartupStudioIds__N` entries,
mutations queue only the affected target, stale generations are discarded, and the
bounded queue reports drops instead of blocking mutations. Persisted membership is
checked before every viewer-neutral hit. See `docs/caching/phase-7-local-evidence.md`
for the live startup, mutation, and revoked-membership proof.

Useful validation commands:

```text
dotnet test Tests/Tests.csproj
dotnet build api.csproj -c Release
docker compose -f docker-compose.yml config --quiet
```
