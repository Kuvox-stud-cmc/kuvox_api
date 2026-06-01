.DEFAULT_GOAL := help
SHELL := /bin/sh

.PHONY: help restore run watch build test format up down clean

help: ## Show this help.
	@awk 'BEGIN {FS = ":.*##"; printf "Targets:\n"} /^[a-zA-Z_-]+:.*?##/ { printf "  %-12s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)

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
