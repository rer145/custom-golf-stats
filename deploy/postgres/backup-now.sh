#!/usr/bin/env bash
# Takes a full base backup with WAL-G, then applies retention: keeps the newest WALG_RETAIN_FULL full backups (default
# 7) and the WAL needed to restore from the oldest of them to now. Used by backup-scheduler, by deploy/backup/drill.sh,
# and by hand, e.g. right after a restore:
#   docker compose exec pg-backup backup-now
# Needs the data directory (PGDATA, read-only is enough), a connection to the server (PGHOST, PGUSER, PGPASSWORD,
# PGDATABASE) and the WAL-G storage settings (WALG_S3_PREFIX, AWS_*), all from the environment. Prints no secrets.
set -euo pipefail

if [[ "${WALG_S3_PREFIX:-}" == none ]]; then
  echo "backup-now: backups are turned off (WALG_S3_PREFIX=none)" >&2
  exit 1
fi
retain="${WALG_RETAIN_FULL:-7}"
if ! [[ "$retain" =~ ^[1-9][0-9]*$ ]]; then
  echo "backup-now: WALG_RETAIN_FULL must be a positive whole number" >&2
  exit 2
fi

wal-g backup-push "${PGDATA:?PGDATA is not set}" --full
wal-g delete retain FULL "$retain" --confirm
