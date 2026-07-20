#!/usr/bin/env python3
"""Reproducible Phase 0 local baseline for the Kuvox API and AI service.

Run from a fully healthy local Kuvox stack. The script intentionally creates an
isolated Studio/project/media graph and removes it in a finally block unless
``--retain-fixture`` is supplied for a later cache-evidence run.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import platform
import re
import subprocess
import sys
import tempfile
import time
from collections.abc import Callable
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import httpx

METRIC_PREFIXES = (
    "kuvox_http_request_duration_seconds_",
    "kuvox_http_requests_total",
    "kuvox_postgres_commands_total",
    "kuvox_postgres_command_duration_seconds_",
    "kuvox_retrieval_stage_calls_total",
    "kuvox_retrieval_stage_duration_seconds_",
)
SAMPLE_RE = re.compile(
    r"^(?P<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(?P<labels>\{[^}]*\})?\s+(?P<value>[-+0-9.eE]+)$"
)


class BaselineError(RuntimeError):
    pass


def main() -> int:
    args = parse_args()
    if args.cleanup_fixture is not None:
        return cleanup_retained_fixture(args)

    created: dict[str, str] = {}
    cleanup_errors: list[str] = []
    cleanup_results: list[str] = []
    results: list[dict[str, Any]] = []
    evidence: dict[str, Any] = {}
    failure: Exception | None = None
    started = datetime.now(UTC)
    with tempfile.TemporaryDirectory(prefix="kuvox-phase0-") as temp_dir:
        video_path = Path(temp_dir) / "phase0-ocr.mp4"
        generate_video(video_path)
        api = httpx.Client(base_url=args.api_url, timeout=args.timeout)
        ai = httpx.Client(base_url=args.ai_url, timeout=args.timeout)
        try:
            token = login(api, args.email, args.password)
            api.headers["Authorization"] = f"Bearer {token}"
            suffix = started.strftime("%Y%m%d-%H%M%S")
            studio = request_json(
                api, "POST", "/api/auth/studios", json={"name": f"Phase 0 Baseline {suffix}"}
            )
            studio_id = studio["id"]
            created["studio_id"] = studio_id
            # Studio memberships are embedded in the access token. Re-authenticate so
            # the disposable Studio is present in the trusted workspace claims.
            token = login(api, args.email, args.password)
            api.headers["Authorization"] = f"Bearer {token}"
            project = request_json(
                api,
                "POST",
                f"/api/projects?studioId={studio_id}",
                json={
                    "kind": 0,
                    "name": f"Phase 0 Video {suffix}",
                    "description": "Disposable cache baseline",
                },
            )
            project_id = project["id"]
            created["project_id"] = project_id
            with video_path.open("rb") as media_file:
                media = request_json(
                    api,
                    "POST",
                    f"/api/media?studioId={studio_id}",
                    data={"kind": "Video", "filename": video_path.name},
                    files={"file": (video_path.name, media_file, "video/mp4")},
                )
            media_id = media["id"]
            created["media_id"] = media_id
            ingested_media = wait_for_media(api, media_id, args.ingestion_timeout)
            request_json(
                api, "POST", f"/api/projects/{project_id}/media", json={"mediaIds": [media_id]}
            )
            project_media = request_json(api, "GET", f"/api/projects/{project_id}/media")
            search_revision = project_media_search_revision(project_media, media_id)
            if search_revision is None or search_revision <= 0:
                raise BaselineError(
                    "Attached project-media DTO did not expose a positive searchRevision."
                )
            evidence["ingestion"] = {
                "status": ingested_media.get("status"),
                "pipeline_terminal": ingested_media.get("pipeline", {}).get("terminal"),
                "search_revision": search_revision,
            }
            save_initial_timeline(api, project_id, media_id)

            groups: list[tuple[str, Callable[[], Any]]] = [
                ("user_settings", lambda: request_json(api, "GET", "/api/auth/me/settings")),
                (
                    "studio_workspace_settings_usage",
                    lambda: request_many(
                        api,
                        [
                            f"/api/auth/studios/{studio_id}/settings/workspace",
                            f"/api/auth/studios/{studio_id}/settings/notifications",
                            f"/api/auth/studios/{studio_id}/usage",
                        ],
                    ),
                ),
                (
                    "project_list_detail_attached_media",
                    lambda: request_many(
                        api,
                        [
                            f"/api/projects?studioId={studio_id}",
                            f"/api/projects/{project_id}",
                            f"/api/projects/{project_id}/media",
                        ],
                    ),
                ),
                (
                    "media_list_storage_usage",
                    lambda: request_many(
                        api,
                        [
                            f"/api/media?studioId={studio_id}",
                            f"/api/media/storage-usage?studioId={studio_id}",
                        ],
                    ),
                ),
                (
                    "task_milestones_labels",
                    lambda: request_many(
                        api,
                        [
                            f"/api/tasks/milestones?studioId={studio_id}",
                            f"/api/tasks/labels?studioId={studio_id}",
                        ],
                    ),
                ),
                (
                    "notification_unread_count",
                    lambda: request_json(api, "GET", "/api/notifications/unread-count"),
                ),
                (
                    "combined_editor_bootstrap",
                    lambda: request_many(
                        api,
                        [
                            f"/api/projects/{project_id}",
                            f"/api/projects/{project_id}/media",
                            f"/api/timelines/projects/{project_id}/current",
                        ],
                    ),
                ),
                (
                    "trusted_fastapi_retrieval",
                    lambda: request_result_json(
                        ai,
                        "POST",
                        "/retrieval/video-editor",
                        json={
                            "projectId": project_id,
                            "mediaIds": [media_id],
                            "query": "KUVOX CACHE BASELINE SECOND SCENE",
                            "modalities": ["transcript", "ocr"],
                            "topK": 10,
                            "expandGraph": True,
                        },
                    ),
                ),
            ]
            for name, operation in groups:
                result = measure_group(name, operation, api, ai, args.repetitions)
                results.append(result)
                if result["errors"]:
                    raise BaselineError(
                        f"{name} had {result['errors']} failed repeated requests; baseline is invalid."
                    )

            retrieval = groups[-1][1]()
            if retrieval.get("totalCandidatesConsidered", 0) <= 0 or not any(
                item.get("evidence") for item in retrieval.get("results", [])
            ):
                raise BaselineError(
                    "Generated media did not produce Qdrant/OCR retrieval evidence; baseline is invalid."
                )
            validate_metric_evidence(results, args.repetitions)
            evidence["retrieval"] = {
                "result_count": len(retrieval.get("results", [])),
                "total_candidates_considered": retrieval.get("totalCandidatesConsidered", 0),
                "evidence_result_count": sum(
                    1 for item in retrieval.get("results", []) if item.get("evidence")
                ),
                "warnings": retrieval.get("warnings", []),
            }
        except Exception as error:  # noqa: BLE001 - report any baseline failure before cleanup
            failure = error
        finally:
            if args.retain_fixture and failure is None:
                write_fixture(args.fixture_file, args, started, created, evidence)
                cleanup_results.append(
                    f"fixture retained for later evidence: {args.fixture_file}"
                )
            else:
                cleanup(api, created, cleanup_errors, cleanup_results)
            api.close()
            ai.close()

    write_report(
        args.output,
        started,
        created,
        results,
        evidence,
        cleanup_errors,
        cleanup_results,
        failure,
    )
    if failure is not None:
        raise failure
    if cleanup_errors:
        print(f"Baseline completed, but cleanup failed for: {created}", file=sys.stderr)
        return 2
    print(f"Baseline report written to {args.output}")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--api-url", default="http://localhost:5280")
    parser.add_argument("--ai-url", default="http://localhost:8000")
    parser.add_argument("--email", default="dev@kuvox.local")
    parser.add_argument("--password", default="Password123!")
    parser.add_argument("--repetitions", type=int, default=20)
    parser.add_argument("--timeout", type=float, default=10)
    parser.add_argument("--ingestion-timeout", type=float, default=300)
    fixture_mode = parser.add_mutually_exclusive_group()
    fixture_mode.add_argument(
        "--retain-fixture",
        action="store_true",
        help="Keep the disposable Studio/project/media graph and write redacted identifiers.",
    )
    parser.add_argument(
        "--fixture-file",
        type=Path,
        default=Path("/tmp/kuvox-cache-fixture.json"),
        help="Redacted retained-fixture metadata path.",
    )
    fixture_mode.add_argument(
        "--cleanup-fixture",
        type=Path,
        help="Delete a previously retained fixture and exit without running the baseline.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).with_name("phase-0-local-baseline.md"),
    )
    return parser.parse_args()


def login(client: httpx.Client, email: str, password: str) -> str:
    response = request_json(
        client,
        "POST",
        "/api/auth/login",
        json={"email": email, "password": password, "replaceExistingSession": True},
    )
    return str(response["accessToken"])


def request_json(client: httpx.Client, method: str, path: str, **kwargs: Any) -> Any:
    response = client.request(method, path, **kwargs)
    if response.status_code >= 400:
        raise BaselineError(
            f"{method} {path} returned {response.status_code}: {response.text[:500]}"
        )
    return None if response.status_code == 204 else response.json()


def request_result_json(client: httpx.Client, method: str, path: str, **kwargs: Any) -> Any:
    """Return the typed payload from FastAPI's explicit ``{result: ...}`` envelope."""
    payload = request_json(client, method, path, **kwargs)
    if not isinstance(payload, dict) or "result" not in payload:
        raise BaselineError(f"{method} {path} did not return the expected result envelope.")
    return payload["result"]


def request_many(client: httpx.Client, paths: list[str]) -> list[Any]:
    return [request_json(client, "GET", path) for path in paths]


def project_media_search_revision(payload: Any, media_id: str) -> int | None:
    if not isinstance(payload, dict) or not isinstance(payload.get("items"), list):
        raise BaselineError("Project-media response did not contain a paged items array.")
    item = next(
        (
            candidate
            for candidate in payload["items"]
            if isinstance(candidate, dict) and str(candidate.get("mediaId")) == media_id
        ),
        None,
    )
    if item is None:
        raise BaselineError("Attached media was absent from the project-media response.")
    revision = item.get("searchRevision")
    return revision if isinstance(revision, int) and not isinstance(revision, bool) else None


def write_fixture(
    path: Path,
    args: argparse.Namespace,
    started: datetime,
    created: dict[str, str],
    evidence: dict[str, Any],
) -> None:
    required = ("studio_id", "project_id", "media_id")
    if any(not created.get(name) for name in required):
        raise BaselineError("Cannot retain an incomplete cache-evidence fixture.")
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "schema_version": 1,
        "created_at": started.isoformat(),
        "api_url": args.api_url,
        "ai_url": args.ai_url,
        "studio_id": created["studio_id"],
        "project_id": created["project_id"],
        "media_id": created["media_id"],
        "search_revision": evidence.get("ingestion", {}).get("search_revision"),
    }
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def cleanup_retained_fixture(args: argparse.Namespace) -> int:
    path = args.cleanup_fixture
    assert path is not None
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise BaselineError(f"Could not read retained fixture {path}: {error}") from error
    if not isinstance(payload, dict) or payload.get("schema_version") != 1:
        raise BaselineError(f"Retained fixture {path} has an unsupported schema.")
    created = {
        name: str(payload.get(name, ""))
        for name in ("studio_id", "project_id", "media_id")
    }
    if any(not value for value in created.values()):
        raise BaselineError(f"Retained fixture {path} is missing required identifiers.")

    api = httpx.Client(base_url=str(payload.get("api_url") or args.api_url), timeout=args.timeout)
    errors: list[str] = []
    results: list[str] = []
    try:
        token = login(api, args.email, args.password)
        api.headers["Authorization"] = f"Bearer {token}"
        cleanup(api, created, errors, results)
    finally:
        api.close()
    for result in results:
        print(result)
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 2
    path.unlink()
    print(f"Removed retained fixture metadata: {path}")
    return 0


def generate_video(path: Path) -> None:
    from PIL import Image, ImageDraw, ImageFont

    font_path = (
        Path(__file__).parents[3]
        / "kuvox_ai_service"
        / "src"
        / "kuvox_ai"
        / "modules"
        / "rendering"
        / "fonts"
        / "Inter.ttf"
    )
    font = ImageFont.truetype(str(font_path), 34)
    scenes = [
        ("#143d59", "white", "KUVOX CACHE BASELINE\nFIRST SCENE"),
        ("#f4b41a", "black", "KUVOX CACHE BASELINE\nSECOND SCENE"),
    ]
    scene_paths: list[Path] = []
    for index, (background, foreground, text) in enumerate(scenes):
        scene_path = path.with_name(f"scene-{index}.png")
        image = Image.new("RGB", (640, 360), background)
        draw = ImageDraw.Draw(image)
        bounds = draw.multiline_textbbox((0, 0), text, font=font, align="center", spacing=12)
        width = bounds[2] - bounds[0]
        height = bounds[3] - bounds[1]
        draw.multiline_text(
            ((640 - width) / 2, (360 - height) / 2),
            text,
            font=font,
            fill=foreground,
            align="center",
            spacing=12,
        )
        image.save(scene_path)
        scene_paths.append(scene_path)

    command = [
        "ffmpeg",
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-loop",
        "1",
        "-framerate",
        "30",
        "-t",
        "2",
        "-i",
        str(scene_paths[0]),
        "-loop",
        "1",
        "-framerate",
        "30",
        "-t",
        "2",
        "-i",
        str(scene_paths[1]),
        "-filter_complex",
        "[0:v][1:v]concat=n=2:v=1:a=0[out]",
        "-map",
        "[out]",
        "-r",
        "30",
        "-c:v",
        "libx264",
        "-pix_fmt",
        "yuv420p",
        "-movflags",
        "+faststart",
        str(path),
    ]
    subprocess.run(command, check=True)


def wait_for_media(client: httpx.Client, media_id: str, timeout: float) -> dict[str, Any]:
    deadline = time.monotonic() + timeout
    last: Any = None
    while time.monotonic() < deadline:
        last = request_json(client, "GET", f"/api/media/{media_id}")
        if str(last.get("status", "")).lower() == "ready" and last.get("pipeline", {}).get(
            "terminal"
        ):
            return last
        if str(last.get("status", "")).lower() == "failed":
            raise BaselineError(f"Media pipeline failed: {last}")
        time.sleep(2)
    raise BaselineError(f"Media did not become ingestion-ready within {timeout}s: {last}")


def save_initial_timeline(client: httpx.Client, project_id: str, media_id: str) -> None:
    now = datetime.now(UTC).isoformat().replace("+00:00", "Z")
    document = {
        "schemaVersion": 1,
        "projectId": project_id,
        "name": "Phase 0 baseline",
        "createdAt": now,
        "updatedAt": now,
        "settings": {"width": 640, "height": 360, "aspectRatio": "16:9", "frameRate": 30},
        "media": {
            media_id: {"id": media_id, "kind": "video", "name": "phase0-ocr.mp4", "duration": 4}
        },
        "tracks": [
            {
                "id": "v1",
                "kind": "video",
                "items": [
                    {
                        "id": "item-1",
                        "type": "video",
                        "mediaId": media_id,
                        "timelineStart": 0,
                        "duration": 4,
                        "sourceIn": 0,
                        "sourceOut": 4,
                    }
                ],
            }
        ],
        "transitions": [],
        "effects": [],
        "history": {"revision": 0, "canUndo": False, "canRedo": False},
    }
    request_json(
        client,
        "PUT",
        f"/api/timelines/projects/{project_id}/current",
        json={
            "documentJson": document,
            "operationsJson": [],
            "baseRevisionNumber": 0,
            "documentSchemaVersion": 1,
            "source": "phase0-baseline",
            "label": "Initial baseline revision",
        },
    )


def measure_group(
    name: str,
    operation: Callable[[], Any],
    api: httpx.Client,
    ai: httpx.Client,
    repetitions: int,
) -> dict[str, Any]:
    warmup_response = operation()
    before = scrape_metrics(api, ai)
    durations: list[float] = []
    response_hashes: list[str] = []
    errors = 0
    for _ in range(repetitions):
        started = time.perf_counter()
        try:
            response_hashes.append(response_hash(operation()))
        except Exception:  # noqa: BLE001 - every request failure contributes to the error count
            errors += 1
        durations.append((time.perf_counter() - started) * 1000)
    after = scrape_metrics(api, ai)
    ordered = sorted(durations)
    warmup_hash = response_hash(warmup_response)
    unique_response_hashes = sorted(set(response_hashes))
    return {
        "name": name,
        "count": repetitions,
        "p50_ms": percentile(ordered, 0.50),
        "p95_ms": percentile(ordered, 0.95),
        "min_ms": min(ordered),
        "max_ms": max(ordered),
        "errors": errors,
        "warmup_response_hash": warmup_hash,
        "response_hashes": unique_response_hashes,
        "response_hash_parity": len(set([warmup_hash, *unique_response_hashes])) <= 1,
        "metric_deltas": {
            key: round(after.get(key, 0) - before.get(key, 0), 6)
            for key in sorted(set(before) | set(after))
            if after.get(key, 0) != before.get(key, 0)
        },
    }


def response_hash(value: Any) -> str:
    canonical = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def validate_metric_evidence(results: list[dict[str, Any]], repetitions: int) -> None:
    retrieval = next(item for item in results if item["name"] == "trusted_fastapi_retrieval")
    qdrant_searches = sum_metric_delta(
        retrieval["metric_deltas"],
        "ai",
        "kuvox_retrieval_stage_calls_total",
        stage="qdrant_search",
        outcome="success",
    )
    kuzu_traversals = sum_metric_delta(
        retrieval["metric_deltas"],
        "ai",
        "kuvox_retrieval_stage_calls_total",
        stage="kuzu_neighbors",
        outcome="success",
    )
    postgres_commands = sum(
        sum_metric_delta(
            item["metric_deltas"], "api", "kuvox_postgres_commands_total"
        )
        for item in results
    )
    if qdrant_searches < repetitions * 2:
        raise BaselineError(
            "Retrieval metrics did not prove both transcript and OCR Qdrant searches "
            f"for every repeated request (observed {qdrant_searches}, expected at least "
            f"{repetitions * 2})."
        )
    if kuzu_traversals < repetitions:
        raise BaselineError(
            "Retrieval metrics did not prove Kuzu graph traversal for every repeated request "
            f"(observed {kuzu_traversals}, expected at least {repetitions})."
        )
    if postgres_commands <= 0:
        raise BaselineError("API metrics did not record any PostgreSQL command activity.")


def sum_metric_delta(
    deltas: dict[str, float],
    service: str,
    metric_name: str,
    **labels: str,
) -> float:
    prefix = f"{service}:{metric_name}"
    return sum(
        value
        for sample, value in deltas.items()
        if sample.startswith(prefix)
        and all(f'{name}="{expected}"' in sample for name, expected in labels.items())
    )


def scrape_metrics(api: httpx.Client, ai: httpx.Client) -> dict[str, float]:
    samples: dict[str, float] = {}
    for service, client in (("api", api), ("ai", ai)):
        response = client.get("/metrics")
        response.raise_for_status()
        for line in response.text.splitlines():
            match = SAMPLE_RE.match(line)
            if not match or not match.group("name").startswith(METRIC_PREFIXES):
                continue
            samples[f"{service}:{match.group('name')}{match.group('labels') or ''}"] = float(
                match.group("value")
            )
    return samples


def percentile(values: list[float], quantile: float) -> float:
    index = max(0, math.ceil(len(values) * quantile) - 1)
    return round(values[index], 3)


def cleanup(
    api: httpx.Client,
    created: dict[str, str],
    errors: list[str],
    results: list[str],
) -> None:
    for kind, soft_path, permanent_path in (
        (
            "project",
            f"/api/projects/{created.get('project_id', '')}",
            f"/api/projects/{created.get('project_id', '')}/permanent",
        ),
        (
            "media",
            f"/api/media/{created.get('media_id', '')}",
            f"/api/media/{created.get('media_id', '')}/permanent",
        ),
    ):
        if not created.get(f"{kind}_id"):
            continue
        try:
            request_json(api, "DELETE", soft_path)
            request_json(api, "DELETE", permanent_path)
            results.append(f"{kind}: soft-deleted and permanently deleted")
        except Exception as error:  # noqa: BLE001 - cleanup is best-effort and fully reported
            errors.append(f"{kind}: {error}")
    if created.get("studio_id"):
        try:
            request_json(api, "DELETE", f"/api/auth/studios/{created['studio_id']}")
            results.append("studio: deleted")
        except Exception as error:  # noqa: BLE001 - cleanup is best-effort and fully reported
            errors.append(f"studio: {error}")


def write_report(
    path: Path,
    started: datetime,
    created: dict[str, str],
    results: list[dict[str, Any]],
    evidence: dict[str, Any],
    cleanup_errors: list[str],
    cleanup_results: list[str],
    error: Exception | None,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# Phase 0 local cache baseline",
        "",
        f"Captured: {started.isoformat()}",
        "",
        "> These local numbers are comparative evidence only. A production-like baseline remains mandatory before broad production rollout.",
        "",
        f"Status: {'failed' if error else 'complete'}",
        "",
        "## Environment",
        "",
        f"- OS: {platform.platform()}",
        f"- Python: {platform.python_version()}",
        f"- FFmpeg: {command_version(['ffmpeg', '-version'])}",
        f"- Docker: {command_version(['docker', '--version'])}",
        f"- .NET: {command_version(['dotnet', '--version'])}",
        "",
        "## Measurements",
        "",
        "| Group | Count | p50 ms | p95 ms | Min ms | Max ms | Errors |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for result in results:
        lines.append(
            f"| {result['name']} | {result['count']} | {result['p50_ms']} | {result['p95_ms']} | "
            f"{round(result['min_ms'], 3)} | {round(result['max_ms'], 3)} | {result['errors']} |"
        )
    lines.extend(["", "## Response hashes", ""])
    for result in results:
        lines.extend(
            [
                f"### {result['name']}",
                "",
                f"- Warm-up: `{result['warmup_response_hash']}`",
                f"- Repeated unique hashes: `{json.dumps(result['response_hashes'])}`",
                f"- Repeated response parity: `{str(result['response_hash_parity']).lower()}`",
                "",
            ]
        )
    lines.extend(
        [
            "## Retrieval and ingestion evidence",
            "",
            "```json",
            json.dumps(evidence, indent=2, sort_keys=True),
            "```",
            "",
            "## Cleanup",
            "",
        ]
    )
    lines.extend(f"- {item}" for item in cleanup_results)
    if not cleanup_results:
        lines.append("- No disposable resources were created.")
    lines.append("")
    lines.extend(["", "## Metric deltas", ""])
    for result in results:
        lines.extend(
            [
                f"### {result['name']}",
                "",
                "```json",
                json.dumps(result["metric_deltas"], indent=2, sort_keys=True),
                "```",
                "",
            ]
        )
    if error:
        lines.extend(["## Failure", "", f"`{type(error).__name__}: {error}`", ""])
    if cleanup_errors:
        lines.extend(
            [
                "## Cleanup failures",
                "",
                f"Created identifiers: `{json.dumps(created, sort_keys=True)}`",
                "",
            ]
        )
        lines.extend(f"- {item}" for item in cleanup_errors)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def command_version(command: list[str]) -> str:
    try:
        output = subprocess.run(command, check=True, capture_output=True, text=True).stdout
        return output.splitlines()[0]
    except Exception as error:  # noqa: BLE001 - version discovery is optional report metadata
        return f"unavailable ({type(error).__name__})"


if __name__ == "__main__":
    raise SystemExit(main())
