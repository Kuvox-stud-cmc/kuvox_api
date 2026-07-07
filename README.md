# kuvox-api

The business backend of **Kuvox** — a graph-augmented retrieval system for
intelligent video editing. This is an ASP.NET Web API (C#) structured as a
modular monolith that handles user accounts, authentication, project state,
timeline management, file storage orchestration, and job dispatch. It sits
between the frontend clients and the Python AI service.

## Tech stack

- **ASP.NET** (.NET 10)
- **Entity Framework Core** (PostgreSQL provider)
- **PostgreSQL** for persistence
- **Redis** for caching and ephemeral state
- **RabbitMQ** for async job dispatch
- **gRPC** for communication with the Python AI service
- **JWT** for authentication

## Prerequisites

- .NET SDK **10.0+**
- Docker + Docker Compose (for local PostgreSQL, Redis, RabbitMQ)

## Getting started

```bash
# Restore dependencies
dotnet restore

# Create the local SeaweedFS S3 auth file, then replace the placeholder secrets.
cp ../infra/seaweedfs/s3.json.example ../infra/seaweedfs/s3.json

# Start local infra (Postgres, Redis, RabbitMQ, SeaweedFS)
docker compose up -d

# Run the API with hot-reload
dotnet watch run
```

A `Makefile` wraps the common tasks (`make help` to list them):

```bash
make up        # start local infra (Postgres, Redis, RabbitMQ, SeaweedFS)
make watch     # run the API with hot-reload
make build     # Release build
make test      # run tests (no-op until a test project exists)
make down      # stop local infra
```

The API will be available at `https://localhost:5001` (or `http://localhost:5000`).

- Scalar API reference: `/scalar` (Development environment only)
- OpenAPI document: `/openapi/v1.json` (Development environment only)

> The OpenAPI/Scalar endpoints are mapped only when `ASPNETCORE_ENVIRONMENT=Development`.

> **Local infra note:** the `ai-service` repo ships its own compose that also publishes
> Redis, RabbitMQ and SeaweedFS on the same ports. Don't run both repos' infra at once —
> bring up only one set, or stop the other before an end-to-end run.

## Modules

| Module      | Tables (schema)                                                       | Responsibility                                                          |
| ----------- | -------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `Auth`      | `auth.users`, `auth.studios`, `auth.user_studios`, `auth.studio_invitations`, `auth.audit_log_entries` | User registration, login, JWT issuance, refresh, Studio access, invitations, settings, audit |
| `Projects`  | `projects.projects`, `projects.project_images`, `projects.project_audios`, `projects.project_videos` | CRUD for projects and project/media associations                        |
| `Media`     | `media.*`                                                             | Media library metadata, albums, sharing, Studio-scoped media records    |
| `Notifications` | `notifications.notifications`                                    | In-app notifications, unread counts, read/archive/delete lifecycle      |
| `Tasks`     | `tasks.task_issues`, `tasks.task_assignees`, `tasks.task_milestones`, `tasks.task_labels`, `tasks.task_issue_labels` | Studio task, review, milestone, label, and assignment tracking |
| `Timelines` | `timelines.{timelines,timeline_revisions,render_jobs,command_history}`| Timeline state, revisions (JSONB ops), render jobs, NL command history  |
| `Shared`    | — (no tables)                                                         | Shared kernel: `BaseEntity`, common DTOs, MediatR markers, health, 501 handler |

### Timelines V-009 Contract Notes

The video editor uses `GET/PUT /api/timelines/projects/{projectId}/current` as its
canonical load/save surface. Saves are snapshot-first: `documentJson` is the
authoritative video timeline document, while `operationsJson` is an audit array
for client-side operation batches. Writes must target video projects, require
project write access, and use `baseRevisionNumber` for optimistic concurrency.

`POST /api/timelines/{timelineId}/render` queues a `render_jobs` row for the
latest synced revision and returns no direct object-storage URL in V-009. It
validates the route/body timeline id, requested latest revision, project write
access, video project kind, and MVP export settings. Rendering dispatch and
worker callbacks are intentionally out of scope for this slice.

Server command-history endpoints are not part of V-009. The future backend
contract should store command history by `projectId` and `userId`, include the
command text plus intent/status/result metadata and timestamps, and scope reads
and writes through the same project read/write permission checks. The frontend
continues to use Dexie-backed command history until that server surface exists.

This API is a **modular monolith** built for later extraction into services. Four rules
are enforced (by convention within the single assembly — see the caveat below):

1. **No direct class imports across modules** — a module may reference only another
   module's `Contracts/` namespace (public interfaces, event records, shareable DTOs).
   All implementation types are `internal`.
2. **Internal module APIs as interfaces** — each module exposes `I{Module}Api` in
   `Contracts/` (e.g. `IAuthApi`); the implementation is `internal`.
3. **Each module owns its own tables** — one `{Module}DbContext` per module, pinned to a
   dedicated Postgres schema with its own `__EFMigrationsHistory`. Cross-module references
   are stored as bare `Guid` ids — no cross-schema FKs, no cross-module EF navigations.
4. **Cross-module events via MediatR** — events are `INotification` records in the
   publisher's `Contracts/`; subscribers implement `INotificationHandler<>` internally
   (e.g. `MediaDeletedEvent` -> Projects association cleanup handlers).

Tasks follows the intended convention: its public cross-module surface lives in
`Modules/Tasks/Contracts`, its implementation types are internal, and assignment/review
notification rows are created by Notifications handlers subscribed to Tasks MediatR
events rather than by direct Tasks -> Notifications API calls.

### Implementation status

The main Auth, Studio, Projects, Media, Timelines, and Notifications controllers are backed
by service/repository code. Studio access uses fixed roles (`Owner`, `Admin`, `Member`,
`Viewer`), email invitations, settings, audit-log APIs, and notification events.
In `Development`, module DbContexts auto-migrate on startup, so `docker compose up` +
`dotnet watch run` applies pending EF migrations without manual `dotnet ef` steps.

Media uploads are library-scoped, not project-bound. The API stores raw uploads in
SeaweedFS under `media/{mediaId}/raw/original{ext}`, saves the media row as `Uploaded`,
and enqueues `media.optimization.requested` through `shared.outbox_messages` without a
`projectId`. Project/media membership is owned by the Projects module through the TPC
`ProjectMedia` hierarchy: `projects.project_images`, `projects.project_audios`, and
`projects.project_videos`.

The API also consumes `media.optimization.completed` and `media.optimization.failed`
from RabbitMQ queues `api.media.optimization.completed` and
`api.media.optimization.failed`. Completion stores canonical/proxy/thumbnail object
keys and bucket names, marks images/audio `Ready`, and moves videos to `Processing`
while enqueueing `ingestion.requested`. Raw storage is deleted only after optimized
metadata is persisted. Failure marks media `Failed` while keeping raw objects for
inspection or later cleanup unless the media has already advanced to `Processing` or
`Ready`.

The API includes a media pipeline recovery hosted service. On startup and every
configured interval, it scans stale live media in `Uploaded` or `Processing`, revives
the stable outbox row for the current stage, and updates `UpdatedAt` so the same row is
not immediately requeued again. Stale `Uploaded` rows with raw object metadata are
requeued to `media.optimization.requested`; stale processing videos with canonical
metadata are requeued to `ingestion.requested`; stale processing images/audio with
canonical metadata are marked `Ready`. Rows without enough raw/canonical metadata are
marked `Failed` with a recovery error.

The FastAPI AI service process does not consume these jobs by itself. For end-to-end
media processing, keep the AI media optimization worker running; for videos, keep the
AI ingestion worker running as well. In RabbitMQ, `media.optimization.requested` should
show at least one consumer while uploads are being optimized.

> **Single-assembly caveat:** because every module lives in one `api.csproj`, `internal`
> and the `Contracts`-only boundary are conventions, not compiler-enforced isolation. To
> make Rule 1 a build-time check, add an architecture test (e.g. NetArchTest) asserting no
> module depends on another module's non-`Contracts` namespace. Tasks is the current
> example of the intended convention inside the single assembly.

## Environment variables

Configuration is loaded from `appsettings.json`, `appsettings.{Environment}.json`,
and environment variables. Key variables:

For local native development, copy `.env.example` to `api/.env`.

- Linux: keep infra hosts on `localhost`; Compose publishes Postgres, Redis,
  RabbitMQ, and SeaweedFS ports to the host.
- macOS: use the same `localhost` values as Linux.
- Windows: use the same `localhost` values as Linux/macOS; the API has no
  OS-specific local scratch paths.
- Docker: do not use `localhost` for infra from inside the API container.

If the API itself runs inside Docker Compose, `localhost` means the API container,
not the infra containers. Use Compose service names instead:

```env
ConnectionStrings__Postgres=Host=postgres;Port=5432;Database=kuvox;Username=kuvox;Password=kuvox
ConnectionStrings__Redis=redis:6379
RabbitMQ__HostName=rabbitmq
Storage__Endpoint=http://seaweedfs-s3:8333
```

For production, prefer real environment variables or a secret manager over committing
`.env` files. Keep one infra set active at a time: use either the workspace-root
Compose file for full-stack development or a per-repo Compose file for isolated work.

| Variable                      | Description                            | Default                            |
| ----------------------------- | -------------------------------------- | ---------------------------------- |
| `ConnectionStrings__Postgres` | PostgreSQL connection string           | see `appsettings.Development.json` |
| `ConnectionStrings__Redis`    | Redis connection string                | `localhost:6379`                   |
| `RabbitMQ__Host`              | RabbitMQ hostname                      | `localhost`                        |
| `Jwt__Secret`                 | JWT signing key                        | —                                  |
| `Jwt__Issuer`                 | JWT issuer claim                       | `kuvox-api`                        |
| `Storage__Endpoint`           | S3-compatible object storage endpoint  | `http://localhost:8333`            |
| `Storage__Region`             | S3 signing region                      | `us-east-1`                        |
| `Storage__AccessKey`          | API S3 access key                      | —                                  |
| `Storage__SecretKey`          | API S3 secret key                      | —                                  |
| `Storage__RawBucketName`      | Raw upload bucket                      | `kuvox-raw`                        |
| `Storage__CreateBucket`       | Allow API to create upload bucket      | `false`                            |
| `MediaPipelineRecovery__Enabled` | Enable stale media recovery         | `true`                             |
| `MediaPipelineRecovery__StaleAfterMinutes` | Stale age for `Uploaded`/`Processing` media | `15`              |
| `MediaPipelineRecovery__PollIntervalSeconds` | Recovery poll interval       | `60`                               |
| `MediaPipelineRecovery__BatchSize` | Max media rows reconciled per pass  | `100`                              |
| `AiService__GrpcUrl`          | gRPC endpoint of the Python AI service | `http://localhost:50051`           |

Storage credentials are required server-side and should be supplied via `.env`,
environment variables, or a secret manager. The browser uploads only through
`/bff/media/upload` -> `/api/media`; do not put `Storage__*`, `AWS_*`, or
SeaweedFS credentials in frontend env files.

## Docker

```bash
docker build -t kuvox-api .
docker run -p 5000:5000 kuvox-api
```

## Related repositories

- **[kuvox-frontend](../frontend)** — React web frontend
- **[kuvox-ai](../ai-service)** — Python AI / media service
- **[kuvox-mobile](../mobile)** — React Native (Expo) mobile client
