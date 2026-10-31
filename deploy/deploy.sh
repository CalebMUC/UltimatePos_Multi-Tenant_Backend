#!/usr/bin/env bash
# Releases what is on main: backs up the database, pulls all three repositories, builds, migrates, restarts, then checks the API answers.
# Run it on the server: /opt/ultimatepos/backend/deploy/deploy.sh   (outside shop hours: the API restarts for a few seconds)
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
env_file="${POS_ENV_FILE:-$root/.env}"
site="$(grep -E '^SITE_ADDRESS=' "$env_file" | tail -n 1 | cut -d= -f2-)"

echo "== 1/5 Backing up the database"
"$here/backup.sh"

echo "== 2/5 Pulling the latest main"
git -C "$root/backend" pull --ff-only
git -C "${FRONTEND_DIR:-$root/frontend}" pull --ff-only
git -C "${WEBSITE_DIR:-$root/website}" pull --ff-only

echo "== 3/5 Building the images"
# One at a time: building them at once can run a 4 GB server out of memory.
"$here/pos.sh" build api
"$here/pos.sh" build web
"$here/pos.sh" build site

echo "== 4/5 Updating the database"
"$here/pos.sh" run --rm migrate

echo "== 5/5 Restarting"
"$here/pos.sh" up -d --remove-orphans

for attempt in $(seq 1 30); do
  if curl -fsS "https://$site/api/v1/health" > /dev/null 2>&1; then
    echo "Released. https://$site is up."
    exit 0
  fi
  sleep 2
done
echo "The API did not answer https://$site/api/v1/health within a minute. Check: $here/pos.sh logs --tail 200 api" >&2
exit 1
