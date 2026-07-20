# Phase 8 local evidence

Captured 2026-07-19 with committed coalescing, metrics, and HTTP-validator defaults
still disabled. The runner writes only aggregate counts, statuses, byte sizes,
latencies, metric samples, and response hashes; bearer tokens, cookies, user IDs,
session IDs, Studio IDs, project IDs, queries, and response bodies are omitted.

The reproducible runner is
`kuvox_frontend/scripts/phase8-cache-evidence.mjs`; its redacted output is
`kuvox_frontend/docs/evidence/cache/phase-8-local-evidence.json`.

## Authenticated SSR duplicate reads

The production frontend server rendered authenticated `/dashboard/reviews`
through the real API. With coalescing disabled, one SSR request issued two
`/api/auth/me` reads and two `/api/auth/me/studios` reads. With coalescing enabled,
each pair became one upstream read. The HTML response hash was unchanged.

## Trusted retrieval lookup coalescing and isolation

Sixteen simultaneous authenticated browser-facing BFF retrievals returned HTTP
200, the same 86 response bytes, and one response hash. Their trusted project-media
lookup fell from one per request to one shared upstream call.

Two different users produced two project-media lookups, and one user with two
different session IDs also produced two lookups. The process-local test separately
started two Node processes and observed one leader plus one joiner in each process,
with no cross-process state or correctness dependency.

## Bounded behavior and metrics

Deterministic local delays and failures captured an active in-flight gauge of one,
15 retrieval joiners, bypass behavior for an expired JWT, a bounded upstream
timeout, and an upstream failure. Automated tests additionally cover caller-abort
independence, capacity fail-open behavior, cleanup, retry, canonical isolation,
JSON cloning, and the absence of tokens and raw identity claims from registry keys,
logs, and metrics.

## Authorized HTTP validators

The real API returned strong ETags and `private, no-cache` for current timeline and
image documents after authorization. Strong, weak, list, and wildcard conditions
are covered by tests. Live conditional reads returned bodyless HTTP 304 responses
through both the API and BFF. After timeline and image mutations, the old validators
returned HTTP 200 with new ETags. A persisted Studio membership removal caused the
same member's conditional timeline API read and image BFF read to return HTTP 403,
never 304; the seed membership was restored during cleanup.

## L1 and HTTP cache decisions

Five sequential warm BFF retrievals produced five project-media lookups, one stable
86-byte response representation, and latencies of 105.20–115.75 ms (110.51 ms
median) in the intentionally delayed harness. Persistent BFF L1 is declined because
the measured payload does not justify another invalidation owner and per-replica
staleness boundary.

The same public favicon bytes and hash were served with and without a cookie using
`public, max-age=604800, stale-while-revalidate=86400`. Authenticated dynamic HTML
remained `no-store`. No new CDN behavior was added.

## Final gates

- API: 82 tests passed; Release build succeeded with zero warnings.
- Frontend: 100 tests passed; typecheck and production build succeeded.
- Compose: both production Compose examples passed `config --quiet` using the
  synchronized root environment example.
- Phase 9 and the broad production-readiness gate remain unchecked.

## Reproduction and rollback

Build the frontend, run the API with `Caching__HttpValidatorsEnabled=true`, then:

```bash
cd kuvox_frontend
node scripts/phase8-cache-evidence.mjs --api-url http://127.0.0.1:5283
```

The runner uses disposable projects, restores the temporarily removed seed
membership, and redacts its output. Rollback requires only setting
`KUVOX_BFF_COALESCING_ENABLED=false` and
`Caching__HttpValidatorsEnabled=false`; no persistent BFF data or Phase 8 Redis keys
exist.
