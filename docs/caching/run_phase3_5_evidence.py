#!/usr/bin/env python3
"""Capture authenticated disabled/cold/warm/outage evidence for API caches."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import statistics
import time
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import httpx
import redis

SAMPLE_RE = re.compile(
    r"^(?P<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(?P<labels>\{[^}]*\})?\s+(?P<value>[-+0-9.eE]+)$"
)
METRIC_NAMES = (
    "kuvox_postgres_commands_total",
    "kuvox_postgres_command_duration_seconds_count",
    "kuvox_postgres_command_duration_seconds_sum",
    "kuvox_business_cache_operations_total",
    "kuvox_business_cache_generation_operations_total",
    "kuvox_redis_commands_total",
    "kuvox_single_flight_events_total",
)


class EvidenceError(RuntimeError):
    pass


def main() -> int:
    args = parse_args()
    fixture = json.loads(args.fixture.read_text(encoding="utf-8"))
    ids = {name: fixture[name] for name in ("studio_id", "project_id", "media_id")}
    groups = endpoint_groups(ids)
    token = login(args.disabled_api_url, args)
    clients = {
        "disabled": token_client(args.disabled_api_url, token, args.timeout),
        "enabled": token_client(args.enabled_api_url, token, args.timeout),
        "outage": token_client(args.outage_api_url, token, args.timeout),
    }
    report: dict[str, Any] = {
        "captured_at": datetime.now(UTC).isoformat(),
        "fixture": {
            "schema_version": fixture.get("schema_version"),
            "search_revision": fixture.get("search_revision"),
        },
        "repetitions": args.repetitions,
        "groups": {},
    }
    try:
        for name, operation in groups:
            disabled = measure(clients["disabled"], operation, args.repetitions)
            cold = measure(clients["enabled"], operation, 1)
            warm = measure(clients["enabled"], operation, args.repetitions)
            outage = measure(clients["outage"], operation, args.repetitions)
            validate_group(name, disabled, cold, warm, outage)
            report["groups"][name] = {
                "disabled": disabled,
                "cold": cold,
                "warm": warm,
                "redis_unavailable": outage,
            }
        report["redis"] = redis_snapshot(args.redis_url, args.redis_key_pattern)
    finally:
        for client in clients.values():
            client.close()

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Phase 3-5 evidence written to {args.output}")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, default=Path("/tmp/kuvox-cache-fixture.json"))
    parser.add_argument("--disabled-api-url", default="http://127.0.0.1:5280")
    parser.add_argument("--enabled-api-url", default="http://127.0.0.1:5281")
    parser.add_argument("--outage-api-url", default="http://127.0.0.1:5282")
    parser.add_argument("--redis-url", default="redis://127.0.0.1:6379/0")
    parser.add_argument("--redis-key-pattern", default="kuvox:v1:api:*")
    parser.add_argument("--email", default="dev@kuvox.local")
    parser.add_argument("--password", default="Password123!")
    parser.add_argument("--repetitions", type=int, default=10)
    parser.add_argument("--timeout", type=float, default=10)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).with_name("phase-3-5-local-evidence.json"),
    )
    return parser.parse_args()


def endpoint_groups(ids: dict[str, str]) -> list[tuple[str, list[str]]]:
    studio_id = ids["studio_id"]
    project_id = ids["project_id"]
    media_id = ids["media_id"]
    return [
        ("user-profile-settings", ["/api/auth/me", "/api/auth/me/settings"]),
        (
            "studio-settings-references",
            [
                f"/api/auth/studios/{studio_id}/settings/workspace",
                f"/api/auth/studios/{studio_id}/settings/notifications",
                f"/api/auth/studios/{studio_id}/roles",
                f"/api/auth/studios/{studio_id}/permissions",
            ],
        ),
        (
            "task-references",
            [
                f"/api/tasks/milestones?studioId={studio_id}",
                f"/api/tasks/labels?studioId={studio_id}",
            ],
        ),
        ("notification-count", ["/api/notifications/unread-count"]),
        ("studio-usage", [f"/api/auth/studios/{studio_id}/usage"]),
        (
            "project-reads",
            [
                f"/api/projects?studioId={studio_id}",
                f"/api/projects/{project_id}",
                f"/api/projects/{project_id}/media",
            ],
        ),
        (
            "media-reads",
            [
                f"/api/media?studioId={studio_id}",
                f"/api/media/{media_id}",
                f"/api/media/storage-usage?studioId={studio_id}",
            ],
        ),
        ("album-lists", [f"/api/albums?studioId={studio_id}"]),
        ("task-lists", [f"/api/tasks?studioId={studio_id}"]),
        (
            "editor-documents",
            [
                f"/api/timelines/projects/{project_id}/current",
                f"/api/timelines?projectId={project_id}",
                f"/api/projects/{project_id}/editor-bootstrap",
            ],
        ),
    ]


def login(base_url: str, args: argparse.Namespace) -> str:
    client = httpx.Client(base_url=base_url, timeout=args.timeout)
    try:
        response = client.post(
            "/api/auth/login",
            json={
                "email": args.email,
                "password": args.password,
                "replaceExistingSession": True,
            },
        )
        response.raise_for_status()
        return str(response.json()["accessToken"])
    finally:
        client.close()


def token_client(base_url: str, token: str, timeout: float) -> httpx.Client:
    client = httpx.Client(base_url=base_url, timeout=timeout)
    client.headers["Authorization"] = f"Bearer {token}"
    return client


def measure(client: httpx.Client, paths: list[str], repetitions: int) -> dict[str, Any]:
    before = scrape_metrics(client)
    durations: list[float] = []
    payload_bytes: list[int] = []
    hashes: list[str] = []
    statuses: list[int] = []
    for _ in range(repetitions):
        started = time.perf_counter()
        bodies: list[Any] = []
        size = 0
        for path in paths:
            response = client.get(path)
            statuses.append(response.status_code)
            if response.status_code >= 400:
                raise EvidenceError(f"GET {path} returned {response.status_code}: {response.text[:500]}")
            size += len(response.content)
            bodies.append(response.json())
        durations.append((time.perf_counter() - started) * 1000)
        payload_bytes.append(size)
        hashes.append(stable_hash(bodies))
    after = scrape_metrics(client)
    deltas = {
        key: round(after.get(key, 0) - before.get(key, 0), 6)
        for key in sorted(set(before) | set(after))
        if after.get(key, 0) != before.get(key, 0)
    }
    postgres_commands = sum(
        value for key, value in deltas.items() if key.startswith("kuvox_postgres_commands_total")
    )
    return {
        "request_batches": repetitions,
        "http_requests": repetitions * len(paths),
        "statuses": sorted(set(statuses)),
        "response_hashes": sorted(set(hashes)),
        "p50_ms": percentile(durations, 0.50),
        "p95_ms": percentile(durations, 0.95),
        "payload_bytes": {
            "average": round(statistics.fmean(payload_bytes), 3),
            "p95": percentile([float(value) for value in payload_bytes], 0.95),
            "maximum": max(payload_bytes),
        },
        "postgres_commands": postgres_commands,
        "postgres_commands_per_batch": round(postgres_commands / repetitions, 3),
        "metric_deltas": deltas,
    }


def scrape_metrics(client: httpx.Client) -> dict[str, float]:
    response = client.get("/metrics")
    response.raise_for_status()
    samples: dict[str, float] = {}
    for line in response.text.splitlines():
        match = SAMPLE_RE.match(line)
        if not match or match.group("name") not in METRIC_NAMES:
            continue
        key = f"{match.group('name')}{match.group('labels') or ''}"
        samples[key] = float(match.group("value"))
    return samples


def validate_group(
    name: str,
    disabled: dict[str, Any],
    cold: dict[str, Any],
    warm: dict[str, Any],
    outage: dict[str, Any],
) -> None:
    hashes = {
        *disabled["response_hashes"],
        *cold["response_hashes"],
        *warm["response_hashes"],
        *outage["response_hashes"],
    }
    if len(hashes) != 1:
        raise EvidenceError(f"{name} response hashes differ across cache modes: {sorted(hashes)}")
    if warm["postgres_commands_per_batch"] >= disabled["postgres_commands_per_batch"]:
        raise EvidenceError(
            f"{name} warm PostgreSQL reads were not reduced "
            f"({warm['postgres_commands_per_batch']} >= {disabled['postgres_commands_per_batch']})."
        )
    if metric_total(cold, "kuvox_business_cache_operations_total", 'outcome="miss"') <= 0:
        raise EvidenceError(f"{name} cold request did not record a cache miss.")
    if metric_total(cold, "kuvox_business_cache_operations_total", 'operation="write"', 'outcome="success"') <= 0:
        raise EvidenceError(f"{name} cold request did not record a successful cache fill.")
    if metric_total(warm, "kuvox_business_cache_operations_total", 'outcome="hit"') <= 0:
        raise EvidenceError(f"{name} warm requests did not record cache hits.")
    outage_fail_open = metric_total(
        outage, "kuvox_business_cache_operations_total", 'outcome="error"'
    ) + metric_total(
        outage, "kuvox_business_cache_operations_total", 'outcome="bypass"'
    ) + metric_total(
        outage, "kuvox_business_cache_generation_operations_total", 'outcome="error"'
    ) + metric_total(
        outage, "kuvox_business_cache_generation_operations_total", 'outcome="bypass"'
    )
    if outage_fail_open <= 0:
        raise EvidenceError(f"{name} Redis-unavailable requests did not record fail-open errors.")


def metric_total(result: dict[str, Any], name: str, *labels: str) -> float:
    return sum(
        value
        for key, value in result["metric_deltas"].items()
        if key.startswith(name) and all(label in key for label in labels)
    )


def redis_snapshot(url: str, pattern: str) -> dict[str, Any]:
    client = redis.Redis.from_url(url, decode_responses=False)
    keys = list(client.scan_iter(match=pattern, count=500))
    payload_bytes = sum(client.memory_usage(key) or 0 for key in keys)
    info = client.info("memory")
    return {
        "key_pattern": pattern,
        "key_cardinality": len(keys),
        "matched_memory_bytes": payload_bytes,
        "instance_used_memory_bytes": int(info["used_memory"]),
        "evicted_keys": int(client.info("stats").get("evicted_keys", 0)),
    }


def stable_hash(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def percentile(values: list[float], quantile: float) -> float:
    ordered = sorted(values)
    index = max(0, math.ceil(len(ordered) * quantile) - 1)
    return round(ordered[index], 3)


if __name__ == "__main__":
    raise SystemExit(main())
