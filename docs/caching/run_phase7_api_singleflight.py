#!/usr/bin/env python3
"""Capture real authenticated Studio-usage single-flight evidence."""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import re
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import httpx

SAMPLE_RE = re.compile(
    r"^(?P<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(?P<labels>\{[^}]*\})?\s+(?P<value>[-+0-9.eE]+)$"
)
METRICS = (
    "kuvox_postgres_commands_total",
    "kuvox_business_cache_operations_total",
    "kuvox_redis_commands_total",
    "kuvox_single_flight_events_total",
)


class EvidenceError(RuntimeError):
    pass


def main() -> int:
    args = parse_args()
    fixture = json.loads(args.fixture.read_text(encoding="utf-8"))
    path = f"/api/auth/studios/{fixture['studio_id']}/usage"
    token = login(args.api_url, args)
    metric_client = token_client(args.api_url, token, args.timeout)
    try:
        cold = concurrent_measure(metric_client, args.api_url, token, path, args.concurrency, args.timeout)
        warm = concurrent_measure(metric_client, args.api_url, token, path, args.concurrency, args.timeout)
    finally:
        metric_client.close()

    validate(cold, warm, args.concurrency)
    report = {
        "captured_at": datetime.now(UTC).isoformat(),
        "concurrency": args.concurrency,
        "fixture": {"schema_version": fixture.get("schema_version")},
        "cold": cold,
        "warm": warm,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Phase 7 API single-flight evidence written to {args.output}")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, default=Path("/tmp/kuvox-cache-fixture.json"))
    parser.add_argument("--api-url", default="http://127.0.0.1:5281")
    parser.add_argument("--email", default="dev@kuvox.local")
    parser.add_argument("--password", default="Password123!")
    parser.add_argument("--timeout", type=float, default=10)
    parser.add_argument("--concurrency", type=int, default=16)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).with_name("phase-7-api-singleflight.json"),
    )
    return parser.parse_args()


def login(base_url: str, args: argparse.Namespace) -> str:
    with httpx.Client(base_url=base_url, timeout=args.timeout) as client:
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


def token_client(base_url: str, token: str, timeout: float) -> httpx.Client:
    return httpx.Client(
        base_url=base_url,
        timeout=timeout,
        headers={"Authorization": f"Bearer {token}"},
    )


def concurrent_measure(
    metric_client: httpx.Client,
    base_url: str,
    token: str,
    path: str,
    concurrency: int,
    timeout: float,
) -> dict[str, Any]:
    before = scrape_metrics(metric_client)
    headers = {"Authorization": f"Bearer {token}"}

    def request() -> tuple[int, str]:
        response = httpx.get(f"{base_url}{path}", headers=headers, timeout=timeout)
        return response.status_code, stable_hash(response.json())

    with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as executor:
        responses = list(executor.map(lambda _: request(), range(concurrency)))
    after = scrape_metrics(metric_client)
    deltas = {
        key: round(after.get(key, 0) - before.get(key, 0), 6)
        for key in sorted(set(before) | set(after))
        if after.get(key, 0) != before.get(key, 0)
    }
    return {
        "statuses": sorted({status for status, _ in responses}),
        "response_hashes": sorted({digest for _, digest in responses}),
        "metric_deltas": deltas,
    }


def scrape_metrics(client: httpx.Client) -> dict[str, float]:
    response = client.get("/metrics")
    response.raise_for_status()
    samples: dict[str, float] = {}
    for line in response.text.splitlines():
        match = SAMPLE_RE.match(line)
        if not match or match.group("name") not in METRICS:
            continue
        samples[f"{match.group('name')}{match.group('labels') or ''}"] = float(
            match.group("value")
        )
    return samples


def validate(cold: dict[str, Any], warm: dict[str, Any], concurrency: int) -> None:
    if cold["statuses"] != [200] or warm["statuses"] != [200]:
        raise EvidenceError("One or more concurrent Studio-usage requests failed.")
    if len(cold["response_hashes"]) != 1 or cold["response_hashes"] != warm["response_hashes"]:
        raise EvidenceError("Cold/warm concurrent Studio-usage responses were not identical.")
    if total(cold, "kuvox_business_cache_operations_total", 'domain="storage-usage"', 'operation="authoritative"', 'outcome="success"') != 1:
        raise EvidenceError("Cold concurrency did not execute exactly one authoritative usage aggregate.")
    if total(cold, "kuvox_single_flight_events_total", 'component="storage-usage"', 'outcome="leader"') != 1:
        raise EvidenceError("Cold concurrency did not elect exactly one Studio-usage leader.")
    joined = total(cold, "kuvox_single_flight_events_total", 'component="storage-usage"', 'outcome="joined_cache_hit"')
    follower_hits = total(
        cold,
        "kuvox_business_cache_operations_total",
        'domain="storage-usage"',
        'operation="studio-aggregate"',
        'outcome="hit"',
    )
    if follower_hits != concurrency - 1 or joined > follower_hits:
        raise EvidenceError(
            f"Expected {concurrency - 1} follower cache reuses, observed "
            f"{follower_hits} hits, including {joined} join-loop hits."
        )
    if total(warm, "kuvox_business_cache_operations_total", 'domain="storage-usage"', 'outcome="hit"') != concurrency:
        raise EvidenceError("Every subsequent warm request did not hit the usage cache.")
    warm_lock_commands = sum(
        value
        for key, value in warm["metric_deltas"].items()
        if key.startswith("kuvox_redis_commands_total")
        and any(f'command="{command}"' in key for command in ("set_nx_px", "exists", "eval_release"))
    )
    if warm_lock_commands != 0:
        raise EvidenceError(f"Warm usage hits issued {warm_lock_commands} lock commands.")


def total(result: dict[str, Any], name: str, *labels: str) -> float:
    return sum(
        value
        for key, value in result["metric_deltas"].items()
        if key.startswith(name) and all(label in key for label in labels)
    )


def stable_hash(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


if __name__ == "__main__":
    raise SystemExit(main())
