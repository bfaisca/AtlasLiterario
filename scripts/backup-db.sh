#!/usr/bin/env bash
# Backup do banco do Atlas Literário (PostgreSQL do docker compose). Uso: ./scripts/backup-db.sh
# Restaurar (substitui o banco atual!):
#   docker compose cp backups/atlas-XXXX.dump db:/tmp/restore.dump
#   docker compose exec -T db pg_restore -U atlas -d atlas_literario --clean --if-exists /tmp/restore.dump
set -euo pipefail
user="${POSTGRES_USER:-atlas}"
out="backups/atlas-$(date +%Y-%m-%d_%H%M).dump"
mkdir -p backups
docker compose exec -T db pg_dump -U "$user" -Fc atlas_literario -f /tmp/atlas.dump
docker compose cp db:/tmp/atlas.dump "$out"
docker compose exec -T db rm -f /tmp/atlas.dump
echo "Backup salvo em $out"
