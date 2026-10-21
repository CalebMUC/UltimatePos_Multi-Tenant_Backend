#!/usr/bin/env bash
# Backs up the database: a compressed dump here (kept BACKUP_KEEP_DAYS, default 14), copied off the server with rclone to
# BACKUP_REMOTE (e.g. b2:ultimatepos-backups, kept BACKUP_REMOTE_KEEP_DAYS, default 60). Run nightly from cron, and before every
# release (deploy.sh does). Exits non-zero if anything fails, so cron mails the error.
#   crontab -e  ->  0 2 * * * /opt/ultimatepos/backend/deploy/backup.sh >> /opt/ultimatepos/backups/backup.log 2>&1
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
env_file="${POS_ENV_FILE:-$here/../../.env}"
setting() { grep -E "^$1=" "$env_file" | tail -n 1 | cut -d= -f2- || true; }

backup_dir="$(setting BACKUP_DIR)"; backup_dir="${backup_dir:-$here/../../backups}"
keep_days="$(setting BACKUP_KEEP_DAYS)"; keep_days="${keep_days:-14}"
remote="$(setting BACKUP_REMOTE)"
remote_keep_days="$(setting BACKUP_REMOTE_KEEP_DAYS)"; remote_keep_days="${remote_keep_days:-60}"

mkdir -p "$backup_dir"
file="$backup_dir/ultimatepos-$(date -u +%Y-%m-%dT%H%M%SZ).dump"

# pg_dump runs inside the database container, as the database's own user; the custom format (-Fc) is compressed and restores with pg_restore.
"$here/pos.sh" exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB"' > "$file.partial"
mv "$file.partial" "$file"
echo "$(date -u +%FT%TZ) dumped $(du -h "$file" | cut -f1) to $file"

if [[ -n "$remote" ]]; then
  rclone copy "$file" "$remote/"
  rclone delete --min-age "${remote_keep_days}d" "$remote/"
  echo "$(date -u +%FT%TZ) copied to $remote"
else
  echo "$(date -u +%FT%TZ) WARNING: BACKUP_REMOTE is not set, so this backup exists only on this server." >&2
fi

find "$backup_dir" -name 'ultimatepos-*.dump' -mtime "+$keep_days" -delete
