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

# Start local infra
docker compose up -d

# Run the API with hot-reload
dotnet watch run
```

The API will be available at `https://localhost:5001` (or `http://localhost:5000`).

- OpenAPI docs: `https://localhost:5001/openapi/v1.json`

## Modules

| Module           | Responsibility                                                   |
| ---------------- | ---------------------------------------------------------------- |
| `Authentication` | User registration, login, JWT issuance and refresh               |
| `Projects`       | CRUD for projects and video metadata                             |
| `Timelines`      | Timeline state, revisions (JSONB operations), version history    |
| `Media`          | File upload orchestration, thumbnails, metadata extraction (FFmpeg) |

Each module is a self-contained namespace with its own entities, services, and
API controllers. Modules communicate through explicit internal interfaces.

## Environment variables

Configuration is loaded from `appsettings.json`, `appsettings.{Environment}.json`,
and environment variables. Key variables:

| Variable                       | Description                              | Default                          |
| ------------------------------ | ---------------------------------------- | -------------------------------- |
| `ConnectionStrings__Postgres`  | PostgreSQL connection string             | see `appsettings.Development.json` |
| `ConnectionStrings__Redis`     | Redis connection string                  | `localhost:6379`                 |
| `RabbitMQ__Host`               | RabbitMQ hostname                        | `localhost`                      |
| `Jwt__Secret`                  | JWT signing key                          | —                                |
| `Jwt__Issuer`                  | JWT issuer claim                         | `kuvox-api`                      |
| `Storage__Endpoint`            | S3-compatible object storage endpoint    | `http://localhost:9000`          |
| `AiService__GrpcUrl`           | gRPC endpoint of the Python AI service   | `http://localhost:50051`         |

## Docker

```bash
docker build -t kuvox-api .
docker run -p 5000:5000 kuvox-api
```

## Related repositories

- **[kuvox-frontend](../frontend)** — React web frontend
- **[kuvox-ai](../ai-service)** — Python AI / media service
- **[kuvox-mobile](../mobile)** — React Native (Expo) mobile client
