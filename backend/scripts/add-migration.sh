#!/usr/bin/env bash
# Usage: ./scripts/add-migration.sh <MigrationName>
# Creates an EF Core migration in AtlasLiterario.Infrastructure/Persistence/Migrations.
set -euo pipefail
cd "$(dirname "$0")/.."
NAME="${1:?informe o nome da migration, ex.: AddMapShareIndex}"
dotnet tool restore
dotnet ef migrations add "$NAME" \
  --project src/AtlasLiterario.Infrastructure \
  --startup-project src/AtlasLiterario.API \
  --output-dir Persistence/Migrations
