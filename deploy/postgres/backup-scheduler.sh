#!/usr/bin/env bash
# Entry point of the pg-backup sidecar (deploy/compose.yaml): runs backup-now once a day at BACKUP_TIME (HH:MM, UTC,
# default 03:00). When the storage holds no base backup yet (a new deployment, or a new WALG_S3_PREFIX) the first
# backup runs right away, so archived WAL is restorable from the start. A failed run is logged and retried after
# BACKUP_RETRY_SECONDS (default 3600); the scheduler itself never exits on a failure, so a storage outage cannot fail a
# deploy that waits for the stack to be up. With WALG_S3_PREFIX=none (throwaway test stacks only) it just idles.
set -uo pipefail

backup_time="${BACKUP_TIME:-03:00}"
retry_seconds="${BACKUP_RETRY_SECONDS:-3600}"
if ! [[ "$backup_time" =~ ^([01][0-9]|2[0-3]):[0-5][0-9]$ ]]; then
  echo "backup-scheduler: BACKUP_TIME must be HH:MM (UTC), e.g. 03:00" >&2
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
  wait_seconds=$(( next - $(date -u +%s) ))
  if (( wait_seconds > 0 )); then
    log "next base backup at $(date -u -d "@$next" +%Y-%m-%dT%H:%M:%SZ)"
    pause "$wait_seconds"
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
