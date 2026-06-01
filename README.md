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

# Start local infra (Postgres, Redis, RabbitMQ, MinIO)
docker compose up -d

# Run the API with hot-reload
dotnet watch run
```

A `Makefile` wraps the common tasks (`make help` to list them):

```bash
make up        # start local infra (Postgres, Redis, RabbitMQ, MinIO)
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
> Redis, RabbitMQ and MinIO on the same ports. Don't run both repos' infra at once —
> bring up only one set, or stop the other before an end-to-end run.

## Modules

| Module      | Tables (schema)                                                       | Responsibility                                                          |
| ----------- | -------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `Auth`      | `auth.users`                                                          | User registration, login, JWT issuance and refresh                      |
| `Projects`  | `projects.projects`                                                   | CRUD for projects                                                       |
| `Videos`    | `videos.videos`                                                       | File upload orchestration, metadata, ingestion status (the old "Media") |
| `Timelines` | `timelines.{timelines,timeline_revisions,render_jobs,command_history}`| Timeline state, revisions (JSONB ops), render jobs, NL command history  |
| `Shared`    | — (no tables)                                                         | Shared kernel: `BaseEntity`, common DTOs, MediatR markers, health, 501 handler |

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
   (e.g. `ProjectDeletedEvent` → Timelines & Videos cleanup handlers).

### Scaffold status

Each module ships **two controllers**: a working **mockup** controller at
`/api/mock/{resource}` returning canned data (for frontend integration today), and the
**real** controller at `/api/{resource}` whose service layer is scaffolded but not yet
implemented — those endpoints return **`501 Not Implemented`**. In `Development`, all four
DbContexts auto-migrate on startup, so `docker compose up` + `dotnet watch run` yields a
fully schema'd database with no manual `dotnet ef` steps.

> **Single-assembly caveat:** because every module lives in one `api.csproj`, `internal`
> and the `Contracts`-only boundary are conventions, not compiler-enforced isolation. To
> make Rule 1 a build-time check, add an architecture test (e.g. NetArchTest) asserting no
> module depends on another module's non-`Contracts` namespace.

## Environment variables

Configuration is loaded from `appsettings.json`, `appsettings.{Environment}.json`,
and environment variables. Key variables:

| Variable                      | Description                            | Default                            |
| ----------------------------- | -------------------------------------- | ---------------------------------- |
| `ConnectionStrings__Postgres` | PostgreSQL connection string           | see `appsettings.Development.json` |
| `ConnectionStrings__Redis`    | Redis connection string                | `localhost:6379`                   |
| `RabbitMQ__Host`              | RabbitMQ hostname                      | `localhost`                        |
| `Jwt__Secret`                 | JWT signing key                        | —                                  |
| `Jwt__Issuer`                 | JWT issuer claim                       | `kuvox-api`                        |
| `Storage__Endpoint`           | S3-compatible object storage endpoint  | `http://localhost:9000`            |
| `AiService__GrpcUrl`          | gRPC endpoint of the Python AI service | `http://localhost:50051`           |

## Docker

```bash
docker build -t kuvox-api .
docker run -p 5000:5000 kuvox-api
```

## Related repositories

- **[kuvox-frontend](../frontend)** — React web frontend
- **[kuvox-ai](../ai-service)** — Python AI / media service
- **[kuvox-mobile](../mobile)** — React Native (Expo) mobile client
