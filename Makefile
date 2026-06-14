.DEFAULT_GOAL := help
SHELL := /bin/sh

# ── Module DbContext map ──────────────────────────────────────────────
# Each module owns its own DbContext and migration folder (Rule 3).
AUTH_CTX        := Kuvox.Api.Modules.Auth.Repositories.AuthDbContext
AUTH_DIR        := Modules/Auth/Repositories/Migrations

MEDIA_CTX       := Kuvox.Api.Modules.Media.Repositories.MediaDbContext
MEDIA_DIR       := Modules/Media/Repositories/Migrations

NOTIFICATIONS_CTX := Kuvox.Api.Modules.Notifications.Repositories.NotificationsDbContext
NOTIFICATIONS_DIR := Modules/Notifications/Repositories/Migrations

PROJECTS_CTX    := Kuvox.Api.Modules.Projects.Repositories.ProjectsDbContext
PROJECTS_DIR    := Modules/Projects/Repositories/Migrations

TIMELINES_CTX   := Kuvox.Api.Modules.Timelines.Repositories.TimelinesDbContext
TIMELINES_DIR   := Modules/Timelines/Repositories/Migrations

# ── General targets ───────────────────────────────────────────────────
.PHONY: help restore run watch build test format up down clean
.PHONY: migrate-add-auth migrate-add-media migrate-add-notifications migrate-add-projects migrate-add-timelines
.PHONY: migrate-auth migrate-media migrate-notifications migrate-projects migrate-timelines
.PHONY: migrate-all db-reset

help: ## Show this help.
	@awk 'BEGIN {FS = ":.*##"; printf "Targets:\n"} /^[a-zA-Z_-]+:.*?##/ { printf "  %-20s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)

restore: ## Restore NuGet dependencies.
	dotnet restore

run: ## Run the API on http://localhost:5000.
	dotnet run

watch: ## Run the API with hot-reload.
	dotnet watch run

build: ## Build in Release configuration.
	dotnet build -c Release

test: ## Run the test suite (no-op until a test project exists).
	dotnet test

format: ## Format code with dotnet format.
	dotnet format

up: ## Start local infra (Postgres, Redis, RabbitMQ, MinIO) via docker-compose.
	docker compose up -d

down: ## Stop local infra.
	docker compose down

clean: ## Remove build artifacts.
	dotnet clean

# ── Add migration (requires NAME=<MigrationName>) ────────────────────
migrate-add-auth: ## Add Auth migration.          Usage: make migrate-add-auth NAME=AddFoo
	dotnet ef migrations add $(NAME) --context $(AUTH_CTX) --output-dir $(AUTH_DIR)

migrate-add-media: ## Add Media migration.        Usage: make migrate-add-media NAME=AddBar
	dotnet ef migrations add $(NAME) --context $(MEDIA_CTX) --output-dir $(MEDIA_DIR)

migrate-add-notifications: ## Add Notifications migration. Usage: make migrate-add-notifications NAME=AddBaz
	dotnet ef migrations add $(NAME) --context $(NOTIFICATIONS_CTX) --output-dir $(NOTIFICATIONS_DIR)

migrate-add-projects: ## Add Projects migration.  Usage: make migrate-add-projects NAME=AddQux
	dotnet ef migrations add $(NAME) --context $(PROJECTS_CTX) --output-dir $(PROJECTS_DIR)

migrate-add-timelines: ## Add Timelines migration. Usage: make migrate-add-timelines NAME=AddQuux
	dotnet ef migrations add $(NAME) --context $(TIMELINES_CTX) --output-dir $(TIMELINES_DIR)

# ── Apply pending migrations (per module) ─────────────────────────────
migrate-auth: ## Apply pending Auth migrations.
	dotnet ef database update --context $(AUTH_CTX)

migrate-media: ## Apply pending Media migrations.
	dotnet ef database update --context $(MEDIA_CTX)

migrate-notifications: ## Apply pending Notifications migrations.
	dotnet ef database update --context $(NOTIFICATIONS_CTX)

migrate-projects: ## Apply pending Projects migrations.
	dotnet ef database update --context $(PROJECTS_CTX)

migrate-timelines: ## Apply pending Timelines migrations.
	dotnet ef database update --context $(TIMELINES_CTX)

# ── Bulk operations ──────────────────────────────────────────────────
migrate-all: ## Apply ALL pending migrations (every module).
	dotnet ef database update --context $(AUTH_CTX)
	dotnet ef database update --context $(MEDIA_CTX)
	dotnet ef database update --context $(NOTIFICATIONS_CTX)
	dotnet ef database update --context $(PROJECTS_CTX)
	dotnet ef database update --context $(TIMELINES_CTX)

db-reset: ## Drop & recreate DB, then apply all migrations.
	dotnet ef database drop --context $(AUTH_CTX) --force
	dotnet ef database update --context $(AUTH_CTX)
	dotnet ef database update --context $(MEDIA_CTX)
	dotnet ef database update --context $(NOTIFICATIONS_CTX)
	dotnet ef database update --context $(PROJECTS_CTX)
	dotnet ef database update --context $(TIMELINES_CTX)
	@echo "Database reset complete — all migrations applied."
