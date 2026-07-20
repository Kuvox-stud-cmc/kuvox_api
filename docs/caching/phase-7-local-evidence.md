# Phase 7 local evidence

Captured 2026-07-19 with committed defaults still disabled. Identifiers,
credentials, Redis keys, queries, and response payloads are omitted or replaced by
stable hashes.

## Authenticated Studio usage single-flight

`phase-7-api-singleflight.json` records 16 simultaneous authenticated requests
against real PostgreSQL and Redis:

- all requests returned HTTP 200 with one response hash;
- one `storage-usage` leader performed exactly one authoritative aggregate and one
  cache write;
- 15 contenders joined and reused the filled value;
- PostgreSQL recorded only the one aggregate workload (two media and two project
  commands) in addition to the persisted authorization reads;
- the next 16 requests were cache hits with no `SET NX PX`, `EXISTS`, or Lua release
  commands.

## Browser-facing BFF retrieval single-flight

`kuvox_frontend/docs/evidence/cache/bff-retrieval-singleflight-local.json` records
16 simultaneous authenticated requests through the shipped BFF. The browser body
included untrusted `mediaIds` and `scopeRevision`; the BFF ignored them and resolved
the one ready project media item with PostgreSQL `searchRevision` 1.

- all cold and warm responses returned HTTP 200, two results, and one stable hash;
- one authoritative pipeline encoded the query once, issued exactly two Qdrant
  searches (transcript and OCR), and performed two Kuzu neighbor expansions (one per
  returned result);
- one retrieval result was written and all 15 contenders reused it;
- the next 16 requests produced 16 retrieval-cache hits and no query encoding,
  Qdrant, Kuzu, or lock commands.

## Startup and mutation prewarming

`phase-7-prewarm-local.json` proves an explicit one-Studio startup allowlist and
mutation-triggered prewarming:

- startup queued and successfully completed one Studio-settings and one
  task-reference target, writing both values for each target;
- the next authorized workspace, notification, label, and milestone reads were all
  Redis hits;
- a no-op-equivalent workspace mutation and a disposable label mutation each queued
  the expected target, completed successfully, and made the next authorized reads
  hit Redis;
- a temporary member could read the viewer-neutral warm values while authorized,
  then received HTTP 403 from both Studio settings and task references immediately
  after persisted membership removal;
- the disposable label, membership, and user were removed.

## Redis ACL and deployment evidence

Two temporary Redis 7 containers launched the shipped `entrypoint-single.sh` for
the API and AI users. Each user successfully executed `SET NX PX`, inspected a
positive TTL, received zero from non-owner release, and received one from owner-safe
Lua release. Cross-prefix writes and `FLUSHDB` were denied. The default user remains
disabled and only explicit `+eval` scripting permission is granted.

Both production Compose files render successfully with placeholder secrets.
Deployment workflows copy `entrypoint-single.sh` and start `redis-api` plus
`redis-ai`.

## Final gates

- API: Release build succeeded with zero warnings; 71 tests passed.
- AI: Ruff check/format and mypy passed; 209 non-integration tests passed; six
  non-ACL real-Redis tests passed, and the production ACL contract was replayed
  separately against real Redis 7.
- Frontend: 87 tests, typecheck, and production build passed.
- Migration: `MediaDbContext` reports no model changes after the checked-in search
  revision migration.
- Compose: both production Compose examples passed `config --quiet`.

Graph-neighbor caching remains explicitly declined: retrieval-result hits already
eliminate repeated Kuzu work, and the live warm run recorded zero Kuzu calls.
