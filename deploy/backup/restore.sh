#!/usr/bin/env bash
# Restores the database from WAL-G storage (WALG_S3_PREFIX in deploy/.env) into a NEW, empty Docker volume:
#   deploy/backup/restore.sh [--backup NAME] [--target-time TIMESTAMP] <volume>
#     --backup NAME             base backup to start from (default LATEST; names from `wal-g backup-list`)
#     --target-time TIMESTAMP   stop replaying WAL at this time, e.g. "2026-11-18 14:30:00+00" (default: replay all
#                               archived WAL, i.e. the latest recoverable point)
#     <volume>                  Docker volume to restore into; created when missing, refused when not empty
# It runs the `restore` service of compose.yaml (the Postgres + WAL-G image, profile `restore`) with <volume> mounted
# as its data directory: backup-fetch, archive recovery, promotion, clean shutdown. The running stack and its pgdata
# volume are not touched, and the restore never writes to the backup storage, so it is safe to run at any time, e.g.
# to check a backup. Putting the restored volume into service is a separate, deliberate step: docs/deploy/backups.md.
# Compose settings come from the usual environment variables when set (COMPOSE_PROJECT_NAME, COMPOSE_FILE,
# COMPOSE_ENV_FILES); otherwise compose.yaml and .env in deploy/ are used, as with deploy.sh. The Postgres image is the
# one the deployed stack runs: the content tag of deploy/postgres (postgres-tag.sh), or POSTGRES_TAG when that pins one.
# IMAGE_TAG (default `remote-deploy.sh current`) is only needed because compose.yaml requires it for the other services.
# A failed restore leaves a partly written volume behind: remove it (docker volume rm <volume>) before trying again.
set -euo pipefail

usage() {
  sed -n '2,7p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

backup="LATEST"
target_time=""
volume=""
while (( $# )); do
  case "$1" in
    --backup) [[ $# -ge 2 ]] || usage; backup="$2"; shift 2 ;;
    --target-time) [[ $# -ge 2 ]] || usage; target_time="$2"; shift 2 ;;
    -h|--help) usage ;;
    -*) echo "unknown option: $1" >&2; usage ;;
    *) [[ -z "$volume" ]] || usage; volume="$1"; shift ;;
  esac
done
[[ -n "$volume" ]] || usage
if ! [[ "$volume" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]+$ ]]; then
  echo "restore.sh: not a valid volume name: $volume" >&2
  exit 2
fi

cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [[ -z "${IMAGE_TAG:-}" ]]; then
  IMAGE_TAG="$(./remote-deploy.sh current 2>/dev/null || true)"
fi
if [[ -z "$IMAGE_TAG" ]]; then
  echo "restore.sh: no deployed tag recorded; set IMAGE_TAG to the deployed tag" >&2
  exit 2
fi
export IMAGE_TAG
POSTGRES_CONTENT_TAG="$(./postgres-tag.sh)"
export POSTGRES_CONTENT_TAG

# A volume that already holds anything (the live pgdata volume included) is refused by restore-pgdata before it
# writes a byte.
if ! docker volume inspect "$volume" >/dev/null 2>&1; then
  docker volume create "$volume" >/dev/null
  echo "==> Created volume $volume"
fi

echo "==> Restoring ${backup} into volume $volume${target_time:+ (up to $target_time)}"
docker compose --profile restore run --rm --no-deps \
  --volume "$volume:/var/lib/postgresql/data" \
  --env RESTORE_BACKUP="$backup" \
  --env RESTORE_TARGET_TIME="$target_time" \
  restore

echo "==> Restored into volume $volume"
echo "    Inspect it: docs/deploy/backups.md (\"Look inside a restored volume\")"
echo "    Put it into service: docs/deploy/backups.md (\"Restoring production\"), then take a new base backup."
