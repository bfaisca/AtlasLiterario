#!/usr/bin/env bash
# Promotes/demotes an account by e-mail. There is NO API endpoint for this on purpose (e-mails are not verified).
#
# Usage: ./scripts/promote-moderator.sh <email> [Moderator|User]      (default: Moderator)
#
# Where it runs SQL (first that applies):
#   1. PSQL_URL set        -> psql "$PSQL_URL"            (e.g. postgresql://atlas:senha@localhost:5432/atlas_literario)
#   2. otherwise           -> docker compose exec db psql (service "db" of the root docker-compose.yml;
#                             POSTGRES_USER/atlas and database atlas_literario, as in the compose file)
# The role takes effect for permission checks immediately (read from the database); the user's screen shows it
# after a new login or token refresh (the web app reads it from /api/auth/me and the JWT).
set -euo pipefail

EMAIL="$(printf '%s' "${1:?informe o e-mail da conta}" | tr '[:upper:]' '[:lower:]' | xargs)"
ROLE_NAME="${2:-Moderator}"
case "$ROLE_NAME" in
  Moderator|moderator) ROLE=1; ROLE_NAME=Moderator ;;
  User|user)           ROLE=0; ROLE_NAME=User ;;
  *) echo "Papel inválido: $ROLE_NAME (use Moderator ou User)" >&2; exit 2 ;;
esac

SQL='UPDATE "user" SET role = :role, updated_at = now() WHERE email = :'"'"'email'"'"' RETURNING email, role;'

run_psql() {
  if [ -n "${PSQL_URL:-}" ]; then
    psql "$PSQL_URL" -X -q -v ON_ERROR_STOP=1 -v email="$EMAIL" -v role="$ROLE" -At -F ' => ' "$@"
  else
    cd "$(dirname "$0")/../.."
    docker compose exec -T db sh -c 'psql -U "${POSTGRES_USER:-atlas}" -d atlas_literario -X -q -v ON_ERROR_STOP=1 -v email="$0" -v role="$1" -At -F " => " "${@:2}"' "$EMAIL" "$ROLE" "$@"
  fi
}

RESULT="$(printf '%s\n' "$SQL" | run_psql)"
if [ -z "$RESULT" ]; then
  echo "Nenhuma conta com o e-mail $EMAIL." >&2
  exit 1
fi
echo "OK: $RESULT  (papel solicitado: $ROLE_NAME)"
