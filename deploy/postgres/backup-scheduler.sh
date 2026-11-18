#!/usr/bin/env bash
# Entry point of the pg-backup sidecar (deploy/compose.yaml): runs backup-now once a day at BACKUP_TIME (HH:MM, UTC,
# default 03:00). When the storage holds no base backup yet (a new deployment, or a new WALG_S3_PREFIX) the first
# backup runs right away, so archived WAL is restorable from the start. A failed run is logged and retried after
# BACKUP_RETRY_SECONDS (default 3600); the scheduler itself never exits on a failure, so a storage outage cannot fail a
# deploy that waits for the stack to be up. Every ARCHIVE_CHECK_SECONDS (default 900) it also checks the server's WAL
# archiver and logs a WARNING while its last failure is newer than its last success (segments then pile up in pg_wal on
# the server's disk until the storage is reachable again). With WALG_S3_PREFIX=none (throwaway test stacks only) it
# just idles.
set -uo pipefail

backup_time="${BACKUP_TIME:-03:00}"
retry_seconds="${BACKUP_RETRY_SECONDS:-3600}"
if ! [[ "$backup_time" =~ ^([01][0-9]|2[0-3]):[0-5][0-9]$ ]]; then
  echo "backup-scheduler: BACKUP_TIME must be HH:MM (UTC), e.g. 03:00" >&2
  exit 2
fi
check_seconds="${ARCHIVE_CHECK_SECONDS:-900}"
if ! [[ "$check_seconds" =~ ^[1-9][0-9]*$ ]]; then
  echo "backup-scheduler: ARCHIVE_CHECK_SECONDS must be a positive whole number" >&2
  exit 2
fi
if ! [[ "$retry_seconds" =~ ^[1-9][0-9]*$ ]]; then
  echo "backup-scheduler: BACKUP_RETRY_SECONDS must be a positive whole number" >&2
  exit 2
fi

log() { printf '%s backup-scheduler: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

# Exit promptly on `docker compose stop` (PID 1 ignores SIGTERM unless trapped; the sleeps below are interruptible).
trap 'log "stopping"; exit 0' TERM INT
pause() { sleep "$1" & wait $!; }

check_archiver() {
  local failing
  failing="$(psql -X -q -At -F ', ' -c "
    select 'last failure ' || last_failed_time || ' on ' || last_failed_wal
           || ', last success ' || coalesce(last_archived_time::text, 'never')
           || ', pending segments ' || (select count(*) from pg_ls_archive_statusdir() where name like '%.ready')
    from pg_stat_archiver
    where last_failed_time is not null and (last_archived_time is null or last_failed_time > last_archived_time)" \
    2>/dev/null)" || return 0
  if [[ -n "$failing" ]]; then
    log "WARNING: WAL archiving to the backup storage is FAILING ($failing). Committed changes since then are not backed up and pg_wal keeps growing; check the storage and its keys."
  fi
}

# Sleeps until the epoch time $1, checking the archiver along the way.
sleep_until() {
  local left
  while left=$(( $1 - $(date -u +%s) )); (( left > 0 )); do
    pause $(( left < check_seconds ? left : check_seconds ))
    check_archiver
  done
}

next_daily() {
  local now today
  now="$(date -u +%s)"
  today="$(date -u -d "today $backup_time" +%s)"
  if (( today > now )); then echo "$today"; else date -u -d "tomorrow $backup_time" +%s; fi
}

if [[ "${WALG_S3_PREFIX:-}" == none ]]; then
  log "backups are turned off (WALG_S3_PREFIX=none): idle"
  while true; do pause 86400; done
fi

log "waiting for Postgres at ${PGHOST:-localhost}"
until pg_isready --quiet; do pause 5; done

# `backup-list --json` prints a JSON array of backups; no "backup_name" means none (or the listing failed: try anyway).
if wal-g backup-list --json 2>/dev/null | grep -q '"backup_name"'; then
  next="$(next_daily)"
else
  log "no base backup in storage yet: taking the first one now"
  next="$(date -u +%s)"
fi

while true; do
  if (( next > $(date -u +%s) )); then
    log "next base backup at $(date -u -d "@$next" +%Y-%m-%dT%H:%M:%SZ)"
    sleep_until "$next"
  fi
  log "base backup starting"
  if backup-now; then
    log "base backup finished; retention applied (keeping ${WALG_RETAIN_FULL:-7} full backups)"
    next="$(next_daily)"
  else
    log "base backup FAILED; retrying in ${retry_seconds}s"
    next=$(( $(date -u +%s) + retry_seconds ))
  fi
done
