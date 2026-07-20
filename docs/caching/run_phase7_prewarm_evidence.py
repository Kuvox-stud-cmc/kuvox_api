#!/usr/bin/env python3
"""Capture startup/mutation prewarming and viewer-neutral authorization evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import time
import uuid
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import httpx

SAMPLE_RE = re.compile(
    r"^(?P<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(?P<labels>\{[^}]*\})?\s+(?P<value>[-+0-9.eE]+)$"
)
METRICS = (
    "kuvox_cache_prewarm_operations_total",
    "kuvox_business_cache_operations_total",
    "kuvox_postgres_commands_total",
)


class EvidenceError(RuntimeError):
    pass


def main() -> int:
    args = parse_args()
    fixture = json.loads(args.fixture.read_text(encoding="utf-8"))
    studio_id = fixture["studio_id"]
    owner = authenticated_client(args, args.email, args.password)
    label_id: str | None = None
    second_user_id: str | None = None
    cleanup: list[str] = []
    report: dict[str, Any] = {
        "captured_at": datetime.now(UTC).isoformat(),
        "fixture": {"schema_version": fixture.get("schema_version")},
    }
    try:
        startup = scrape_metrics(owner)
        require_counter(startup, "kuvox_cache_prewarm_operations_total", 1, target="studio-settings", outcome="success")
        require_counter(startup, "kuvox_cache_prewarm_operations_total", 1, target="task-references", outcome="success")
        report["startup_allowlist"] = selected_metrics(startup, "kuvox_cache_prewarm_operations_total")

        startup_reads = measure_reads(owner, read_paths(studio_id))
        require_delta(startup_reads, "kuvox_business_cache_operations_total", 1, domain="studio", operation="workspace-settings", outcome="hit")
        require_delta(startup_reads, "kuvox_business_cache_operations_total", 1, domain="studio", operation="notification-settings", outcome="hit")
        require_delta(startup_reads, "kuvox_business_cache_operations_total", 1, domain="tasks", operation="labels", outcome="hit")
        require_delta(startup_reads, "kuvox_business_cache_operations_total", 1, domain="tasks", operation="milestones", outcome="hit")
        report["startup_authorized_reads"] = redacted_reads(startup_reads)

        workspace = request_json(owner, "GET", f"/api/auth/studios/{studio_id}/settings/workspace")
        before_mutation = scrape_metrics(owner)
        request_json(
            owner,
            "PATCH",
            f"/api/auth/studios/{studio_id}/settings/workspace",
            json={
                "name": workspace["name"],
                "description": workspace.get("description"),
                "avatarUrl": workspace.get("avatarUrl"),
                "publicSlug": workspace.get("publicSlug"),
            },
        )
        label_name = f"phase7-prewarm-{uuid.uuid4().hex[:10]}"
        label = request_json(
            owner,
            "POST",
            f"/api/tasks/labels?studioId={studio_id}",
            json={"name": label_name, "color": "#7c3aed"},
        )
        label_id = str(label["id"])
        mutation_metrics = wait_for_prewarm(owner, before_mutation, studio=1, tasks=1, timeout=args.wait_timeout)
        mutation_reads = measure_reads(owner, read_paths(studio_id))
        labels = mutation_reads["responses"][2]
        if not any(item.get("id") == label_id for item in labels):
            raise EvidenceError("Mutation-prewarmed label list did not contain the new label.")
        report["mutation_prewarm"] = {
            "prewarm_metric_deltas": metric_delta(before_mutation, mutation_metrics),
            "authorized_reads": redacted_reads(mutation_reads),
        }

        before_label_cleanup = scrape_metrics(owner)
        request_json(owner, "DELETE", f"/api/tasks/labels/{label_id}")
        wait_for_prewarm(owner, before_label_cleanup, studio=0, tasks=1, timeout=args.wait_timeout)
        cleanup.append("temporary task label deleted")
        label_id = None

        suffix = uuid.uuid4().hex[:12]
        second_email = f"phase7-{suffix}@example.invalid"
        second_password = "Phase7Evidence123!"
        registered = request_json(
            owner,
            "POST",
            "/api/auth/register",
            json={"email": second_email, "password": second_password, "displayName": "Phase 7 Evidence"},
            authenticated=False,
        )
        second_user_id = str(registered["id"])
        set_verified(args, second_user_id)
        request_json(
            owner,
            "POST",
            f"/api/auth/studios/{studio_id}/members",
            json={"email": second_email, "role": 2},
        )
        second = authenticated_client(args, second_email, second_password)
        try:
            warm_member_reads = measure_reads(
                second,
                [
                    f"/api/auth/studios/{studio_id}/settings/workspace",
                    f"/api/tasks/labels?studioId={studio_id}",
                ],
            )
            if warm_member_reads["statuses"] != [200]:
                raise EvidenceError("Authorized second member could not read viewer-neutral warm values.")

            before_removal = scrape_metrics(owner)
            request_json(owner, "DELETE", f"/api/auth/studios/{studio_id}/members/{second_user_id}")
            wait_for_prewarm(owner, before_removal, studio=1, tasks=0, timeout=args.wait_timeout)
            rejected_statuses = [
                second.get(f"/api/auth/studios/{studio_id}/settings/workspace").status_code,
                second.get(f"/api/tasks/labels?studioId={studio_id}").status_code,
            ]
            if rejected_statuses != [403, 403]:
                raise EvidenceError(
                    f"Revoked membership reached viewer-neutral cache values: {rejected_statuses}."
                )
            report["viewer_neutral_authorization"] = {
                "authorized_statuses": warm_member_reads["statuses"],
                "authorized_metric_deltas": warm_member_reads["metric_deltas"],
                "revoked_statuses": rejected_statuses,
            }
            cleanup.append("temporary Studio membership removed")
        finally:
            second.close()
    finally:
        if label_id is not None:
            try:
                request_json(owner, "DELETE", f"/api/tasks/labels/{label_id}")
                cleanup.append("temporary task label deleted during cleanup")
            except Exception as error:  # noqa: BLE001
                cleanup.append(f"temporary task label cleanup failed: {type(error).__name__}")
        owner.close()
        if second_user_id is not None:
            delete_user(args, second_user_id)
            cleanup.append("temporary evidence user deleted")

    report["cleanup"] = cleanup
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Phase 7 prewarm evidence written to {args.output}")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, default=Path("/tmp/kuvox-cache-fixture.json"))
    parser.add_argument("--api-url", default="http://127.0.0.1:5283")
    parser.add_argument("--email", default="dev@kuvox.local")
    parser.add_argument("--password", default="Password123!")
    parser.add_argument("--timeout", type=float, default=10)
    parser.add_argument("--wait-timeout", type=float, default=15)
    parser.add_argument("--postgres-container", default="cacanode-postgres-1")
    parser.add_argument("--postgres-user", default="cacanode")
    parser.add_argument("--postgres-database", default="cacanode")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).with_name("phase-7-prewarm-local.json"),
    )
    return parser.parse_args()


def authenticated_client(args: argparse.Namespace, email: str, password: str) -> httpx.Client:
    client = httpx.Client(base_url=args.api_url, timeout=args.timeout)
    response = client.post(
        "/api/auth/login",
        json={"email": email, "password": password, "replaceExistingSession": True},
    )
    response.raise_for_status()
    client.headers["Authorization"] = f"Bearer {response.json()['accessToken']}"
    return client


def request_json(
    client: httpx.Client,
    method: str,
    path: str,
    *,
    authenticated: bool = True,
    **kwargs: Any,
) -> Any:
    headers = None if authenticated else {"Authorization": ""}
    response = client.request(method, path, headers=headers, **kwargs)
    if response.status_code >= 400:
        raise EvidenceError(f"{method} {path} returned {response.status_code}: {response.text[:500]}")
    return None if response.status_code == 204 else response.json()


def read_paths(studio_id: str) -> list[str]:
    return [
        f"/api/auth/studios/{studio_id}/settings/workspace",
        f"/api/auth/studios/{studio_id}/settings/notifications",
        f"/api/tasks/labels?studioId={studio_id}",
        f"/api/tasks/milestones?studioId={studio_id}",
    ]


def measure_reads(client: httpx.Client, paths: list[str]) -> dict[str, Any]:
    before = scrape_metrics(client)
    responses: list[Any] = []
    statuses: list[int] = []
    for path in paths:
        response = client.get(path)
        statuses.append(response.status_code)
        if response.status_code >= 400:
            raise EvidenceError(f"GET {path} returned {response.status_code}: {response.text[:500]}")
        responses.append(response.json())
    after = scrape_metrics(client)
    return {
        "statuses": sorted(set(statuses)),
        "responses": responses,
        "metric_deltas": metric_delta(before, after),
    }


def scrape_metrics(client: httpx.Client) -> dict[str, float]:
    response = client.get("/metrics")
    response.raise_for_status()
    samples: dict[str, float] = {}
    for line in response.text.splitlines():
        match = SAMPLE_RE.match(line)
        if not match or match.group("name") not in METRICS:
            continue
        samples[f"{match.group('name')}{match.group('labels') or ''}"] = float(match.group("value"))
    return samples


def wait_for_prewarm(
    client: httpx.Client,
    before: dict[str, float],
    *,
    studio: int,
    tasks: int,
    timeout: float,
) -> dict[str, float]:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        current = scrape_metrics(client)
        delta = metric_delta(before, current)
        studio_success = metric_value(delta, "kuvox_cache_prewarm_operations_total", target="studio-settings", outcome="success")
        task_success = metric_value(delta, "kuvox_cache_prewarm_operations_total", target="task-references", outcome="success")
        if studio_success >= studio and task_success >= tasks:
            return current
        time.sleep(0.1)
    raise EvidenceError("Timed out waiting for cache prewarming success metrics.")


def set_verified(args: argparse.Namespace, user_id: str) -> None:
    run_psql(
        args,
        f'''UPDATE auth.users SET "EmailVerifiedAt" = NOW() WHERE "Id" = '{user_id}'::uuid;''',
    )


def delete_user(args: argparse.Namespace, user_id: str) -> None:
    run_psql(args, f'''DELETE FROM auth.users WHERE "Id" = '{user_id}'::uuid;''')


def run_psql(args: argparse.Namespace, sql: str) -> None:
    subprocess.run(
        [
            "docker",
            "exec",
            args.postgres_container,
            "psql",
            "-U",
            args.postgres_user,
            "-d",
            args.postgres_database,
            "-v",
            "ON_ERROR_STOP=1",
            "-c",
            sql,
        ],
        check=True,
        capture_output=True,
        text=True,
    )


def metric_delta(before: dict[str, float], after: dict[str, float]) -> dict[str, float]:
    return {
        key: round(after.get(key, 0) - before.get(key, 0), 6)
        for key in sorted(set(before) | set(after))
        if after.get(key, 0) != before.get(key, 0)
    }


def selected_metrics(metrics: dict[str, float], prefix: str) -> dict[str, float]:
    return {key: value for key, value in sorted(metrics.items()) if key.startswith(prefix)}


def redacted_reads(result: dict[str, Any]) -> dict[str, Any]:
    responses = result["responses"]
    return {
        "statuses": result["statuses"],
        "response_hashes": [stable_hash(response) for response in responses],
        "item_counts": [len(response) if isinstance(response, list) else 1 for response in responses],
        "metric_deltas": result["metric_deltas"],
    }


def stable_hash(value: Any) -> str:
    payload = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def metric_value(metrics: dict[str, float], name: str, **labels: str) -> float:
    return sum(
        value
        for key, value in metrics.items()
        if key.startswith(name)
        and all(f'{label}="{expected}"' in key for label, expected in labels.items())
    )


def require_counter(metrics: dict[str, float], name: str, minimum: float, **labels: str) -> None:
    observed = metric_value(metrics, name, **labels)
    if observed < minimum:
        raise EvidenceError(f"{name} {labels} observed {observed}, expected at least {minimum}.")


def require_delta(result: dict[str, Any], name: str, expected: float, **labels: str) -> None:
    observed = metric_value(result["metric_deltas"], name, **labels)
    if observed != expected:
        raise EvidenceError(f"{name} {labels} observed {observed}, expected {expected}.")


if __name__ == "__main__":
    raise SystemExit(main())
