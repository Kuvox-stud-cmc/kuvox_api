# Production Redis Phase 0 runbook

Kuvox targets dedicated Redis 7 cache-only nodes for ASP.NET (`redis-api`, 128 MiB)
and FastAPI (`redis-ai`, 384 MiB) on the same Docker host and private Compose network.
Host publishing is loopback-only. Internal TLS is not enabled for this same-host
topology; TLS becomes mandatory if Redis moves off-host.

## Runtime and security contract

- The default ACL user is disabled.
- `kuvox-api` may access only `kuvox:v1:api:*`; `kuvox-ai` may access only
  `kuvox:v1:ai:*`.
- Independent `REDIS_API_PASSWORD` and `REDIS_AI_PASSWORD` secrets are supplied through
  the production environment. The container entrypoint generates `/run/redis/users.acl`
  at startup; the generated ACL file is never committed.
- Both users receive `EVAL` only for owner-safe compare-and-delete release; key-prefix
  ACL isolation still applies to every script key. Dangerous administrative commands
  remain denied.
- AOF is enabled with `appendfsync everysec` for faster warm recovery. Cached values remain
  recomputable and non-authoritative.
- `maxmemory` is 128 MiB for API and 384 MiB for AI with `allkeys-lfu`.
- AI operational targets are 96 MiB text, 32 MiB visual, 64 MiB audio, and 64 MiB
  retrieval results, leaving 128 MiB headroom.
- Short advisory single-flight locks share the appropriate service cache node and
  prefix. Do not share either node with SignalR, queues, sessions, or authoritative
  ephemeral state without a new capacity and failure-policy review.

## Failure policy

Applications do not have Redis health-based startup gating. Five consecutive command
failures open the client circuit for ten seconds; one half-open attempt then tests recovery.
Redis failure, timeout, eviction, OOM, restart, or flush must result in bypass/fallback, not
an otherwise avoidable application failure. Explicit request cancellation still propagates.

## Production-readiness checklist

- [x] Redis 7, loopback-only host binding, private Docker network.
- [x] Per-service ACL users and disabled default user.
- [x] AOF `everysec`, dedicated 128/384 MiB maxima, `allkeys-lfu`.
- [x] Bounded 500 ms connect/operation timeouts and application circuit breakers.
- [x] Credential-redacted endpoint logging.
- [x] Cache-only workload isolation policy and initial memory budgets.
- [x] Versioned `kuvox:v1` key and schema-envelope migration convention.
- [x] Production ACL scripts validated with real Redis for `SET NX PX`, TTL,
  owner-safe Lua release, non-owner rejection, cross-prefix denial, and admin denial.
- [x] Run the checked-in authenticated local baseline and archive valid retrieval evidence.
- [ ] Capture the mandatory production-like baseline before Phase 1 enablement.
- [ ] Exercise OOM, credential rotation, AOF recovery, and host restart in staging.
- [ ] Add dashboards/alerts for memory, evictions, command latency/errors, connections,
  circuit state, and authoritative fallback load.

## Schema migration convention

For incompatible changes, introduce `kuvox:v2` or a domain-specific version, stop old
writes, deploy readers/writers for the new format, and allow old keys to expire. Do not
require a blocking all-key migration and do not broadly dual-write. Rollback compatibility
must be decided before retiring the prior prefix.

## Ownership matrix

| Domain | Owner | Authoritative source | Phase 0 state |
|---|---|---|---|
| ASP.NET DTO candidates | ASP.NET API | PostgreSQL | Contracts only; disabled |
| Query/ingestion embeddings | FastAPI | Model computation and source media/text | Contracts only; disabled |
| Retrieval results | FastAPI, scoped by trusted BFF input | Qdrant + Kuzu + PostgreSQL-derived scope | Implemented; disabled by default |
| Advisory locks | Owning service | Redis acceleration only | Implemented; disabled by default |
| Browser editor recovery | Frontend/Dexie | Server timeline revisions | Unchanged; not Redis |
| Redis deployment/ACL/capacity | Operations | Compose/secrets/runbook | Implemented for single-node target |
