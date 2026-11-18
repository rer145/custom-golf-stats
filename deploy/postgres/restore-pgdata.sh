#!/usr/bin/env bash
# Entry point of the one-off `restore` service (deploy/compose.yaml); run it through deploy/backup/restore.sh.
# Restores PGDATA, which must be empty (a fresh volume), from WAL-G storage:
#   1. backup-fetch of RESTORE_BACKUP (default LATEST);
#   2. archive recovery with restore_command = wal-g wal-fetch, to the end of the archived WAL, or to
#      RESTORE_TARGET_TIME when set (a Postgres timestamp, e.g. "2026-11-18 14:30:00+00");
#   3. promotion, then a clean shutdown. The volume is then an ordinary data directory, ready to be started.
# The temporary server listens on no TCP address and runs with archive_mode=off, so a restore (or a drill) never
# writes into the storage it reads from. After putting a restored volume into service, take a new base backup
# (`docker compose exec pg-backup backup-now`): the new timeline is archived from that backup on.
# Runs as root only to take ownership of the volume; everything else runs as postgres. Prints no secrets.
set -euo pipefail

pgdata="${PGDATA:?PGDATA is not set}"
backup="${RESTORE_BACKUP:-LATEST}"
target_time="${RESTORE_TARGET_TIME:-}"
timeout="${RESTORE_TIMEOUT_SECONDS:-21600}"
db_user="${POSTGRES_USER:-postgres}"

log() { printf '%s restore: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
as_pg() { if [[ "$(id -u)" == 0 ]]; then gosu postgres "$@"; else "$@"; fi; }

if [[ "${WALG_S3_PREFIX:-}" == none ]]; then
  echo "restore: there is no backup storage (WALG_S3_PREFIX=none)" >&2
  exit 1
fi
if ! [[ "$backup" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "restore: RESTORE_BACKUP must be LATEST or a backup name from \`wal-g backup-list\`" >&2
  exit 2
fi

mkdir -p "$pgdata"
if [[ -n "$(ls -A "$pgdata")" ]]; then
  echo "restore: $pgdata is not empty; restore only into a fresh volume" >&2
  exit 1
fi
if [[ "$(id -u)" == 0 ]]; then
  chown postgres:postgres "$pgdata"
fi
chmod 700 "$pgdata"

log "fetching base backup $backup"
as_pg wal-g backup-fetch "$pgdata" "$backup"
as_pg touch "$pgdata/recovery.signal"

args=(-D "$pgdata"
  -c listen_addresses=
  -c archive_mode=off
  -c "restore_command=/usr/local/bin/wal-g wal-fetch %f %p"
  -c recovery_target_action=promote)
if [[ -n "$target_time" ]]; then
  log "replaying archived WAL up to $target_time"
  args+=(-c "recovery_target_time=$target_time")
else
  log "replaying all archived WAL (point in time: latest)"
fi

as_pg postgres "${args[@]}" &
server=$!

in_recovery() { as_pg psql -X -q -At -U "$db_user" -d postgres -c 'select pg_is_in_recovery()' 2>/dev/null; }

deadline=$(( $(date +%s) + timeout ))
until [[ "$(in_recovery)" == f ]]; do
  if ! kill -0 "$server" 2>/dev/null; then
    wait "$server" || true
    echo "restore: Postgres stopped during recovery (see its log above); the volume is not usable" >&2
    exit 1
  fi
  if (( $(date +%s) > deadline )); then
    echo "restore: recovery did not finish within ${timeout}s" >&2
    as_pg pg_ctl -D "$pgdata" -m immediate stop >/dev/null 2>&1 || true
    exit 1
  fi
  sleep 2
done

summary="$(as_pg psql -X -q -At -U "$db_user" -d postgres -F ' ' -c \
  "select 'timeline ' || timeline_id || ', redo ' || redo_lsn from pg_control_checkpoint()")"
log "recovery complete and promoted ($summary)"
as_pg pg_ctl -D "$pgdata" -m fast -w stop >/dev/null
wait "$server" || true
log "restored data directory is ready"
