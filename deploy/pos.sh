#!/usr/bin/env bash
# docker compose for the production stack, with the right compose file and .env. Any docker compose command works:
#   ./pos.sh ps | ./pos.sh up -d | ./pos.sh run --rm migrate | ./pos.sh logs -f api | ./pos.sh restart api
# The .env file is /opt/ultimatepos/.env (two folders up from this one) unless POS_ENV_FILE names another.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
export POS_ENV_FILE="${POS_ENV_FILE:-$here/../../.env}"

if [[ ! -f "$POS_ENV_FILE" ]]; then
  echo "No .env at $POS_ENV_FILE. Copy deploy/.env.example there and fill it in." >&2
  exit 1
fi

exec docker compose -f "$here/docker-compose.prod.yml" --env-file "$POS_ENV_FILE" "$@"
