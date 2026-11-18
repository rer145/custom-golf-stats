#!/usr/bin/env bash
# Backup and restore drill: proves that a database can be rebuilt from WAL-G storage alone.
#  1. Starts MinIO and the production postgres + pg-backup services (deploy/compose.yaml, image built from
#     deploy/postgres), archiving to MinIO. pg-backup takes the first base backup by itself.
#  2. Writes data, takes a base backup with backup-now (retention set to 1, so the first backup must disappear),
#     writes more data (WAL only), notes a point in time T, writes a last batch, and waits until it is archived.
#  3. Destroys the source: the postgres container and its data volume.
#  4. Restores with deploy/backup/restore.sh into a fresh volume, point in time "latest", starts a scratch Postgres on
#     it and checks every table's row count and content checksum, that the server is out of recovery, and that it
#     is on timeline 2.
#  5. Puts that restored volume into service as the runbook does (copied into a new pgdata, archiving on), checks that
#     the new timeline's history file reaches the storage, and writes and archives more WAL on timeline 2.
#  6. Restores the older timeline-1 base backup up to T into another fresh volume while timeline 2 runs: checks the
#     batch written after T is absent, that recovery chose the unused timeline 3, and that the archived WAL in storage
#     is unchanged (nothing of timeline 2 overwritten).
#  7. Tears everything down, volumes included, whatever happened (DRILL_KEEP=1 keeps it for a look around).
# Needs Docker with Compose v2 and openssl. Credentials are generated per run and never printed.
#   deploy/backup/drill.sh
#   DRILL_MINIO_IMAGE=...  use another MinIO image (default: the digest pinned in drill.compose.yaml)
set -euo pipefail

backup_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
deploy_dir="$(dirname "$backup_dir")"
project="cgs-drill"
bucket="drill-backups"
db="custom_golf_stats"
db_user="custom_golf_stats"
image_repo="cgs-drill-postgres"
image_tag="drill"
image="$image_repo:$image_tag"
vol_latest="${project}_restored_latest"
vol_pitr="${project}_restored_pitr"

work="$(mktemp -d)"
chmod 700 "$work"
(
  umask 077
  cat >"$work/.env" <<EOF
WEB_DOMAIN=drill.invalid
API_DOMAIN=api.drill.invalid
ACME_EMAIL=drill@example.com
IMAGE_TAG=$image_tag
# The Postgres image is pinned to its own tag here (POSTGRES_TAG overrides the content tag from postgres-tag.sh).
POSTGRES_TAG=$image_tag
SESSION_SECRET=$(openssl rand -hex 32)
PROXY_SUBNET=172.30.251.0/24
POSTGRES_IMAGE=$image_repo
POSTGRES_DB=$db
POSTGRES_USER=$db_user
POSTGRES_PASSWORD=$(openssl rand -hex 24)
WALG_S3_PREFIX=s3://$bucket/custom-golf-stats
AWS_ACCESS_KEY_ID=drill$(openssl rand -hex 8)
AWS_SECRET_ACCESS_KEY=$(openssl rand -hex 24)
AWS_ENDPOINT=http://minio:9000
AWS_REGION=us-east-1
AWS_S3_FORCE_PATH_STYLE=true
WALG_RETAIN_FULL=1
EOF
  if [[ -n "${DRILL_MINIO_IMAGE:-}" ]]; then echo "DRILL_MINIO_IMAGE=$DRILL_MINIO_IMAGE" >>"$work/.env"; fi
)

# The same settings through the standard Compose variables, for restore.sh.
export COMPOSE_PROJECT_NAME="$project"
export COMPOSE_FILE="$deploy_dir/compose.yaml:$backup_dir/drill.compose.yaml"
export COMPOSE_ENV_FILES="$work/.env"
export IMAGE_TAG="$image_tag"
POSTGRES_CONTENT_TAG="$("$deploy_dir/postgres-tag.sh")"
export POSTGRES_CONTENT_TAG
compose() { docker compose "$@"; }

step() { printf '\n==> %s\n' "$*"; }
fail() { echo "DRILL FAILED: $*" >&2; exit 1; }

teardown() {
  local status=$?
  if (( status != 0 )); then
    echo "--- postgres / pg-backup logs (tail) ---" >&2
    compose logs --no-color --tail=60 postgres pg-backup >&2 2>/dev/null || true
  fi
  if [[ "${DRILL_KEEP:-}" == 1 ]]; then
    echo "Drill left running (project $project, env $work/.env). Tear down with:"
    echo "  COMPOSE_PROJECT_NAME=$project COMPOSE_FILE=$COMPOSE_FILE COMPOSE_ENV_FILES=$work/.env docker compose down -v"
    echo "  docker rm -f ${project}-verify-latest ${project}-verify-pitr; docker volume rm $vol_latest $vol_pitr"
  else
    docker rm -f "${project}-verify-latest" "${project}-verify-pitr" >/dev/null 2>&1 || true
    compose --profile restore down -v --remove-orphans >/dev/null 2>&1 || true
    docker volume rm "$vol_latest" "$vol_pitr" >/dev/null 2>&1 || true
    rm -rf "$work"
  fi
  if (( status == 0 )); then echo "DRILL PASSED"; fi
  exit "$status"
}
trap teardown EXIT

src_sql() { compose exec -T postgres psql -X -q -At -v ON_ERROR_STOP=1 -U "$db_user" -d "$db" "$@"; }

# One line per table: name, row count, md5 of every row in key order. Identical lines = identical data.
snapshot_sql="
select 'drill_rounds', count(*), md5(coalesce(string_agg(r::text, '|' order by id), '')) from drill_rounds r
union all
select 'drill_events', count(*), md5(coalesce(string_agg(e::text, '|' order by id), '')) from drill_events e
order by 1"

# MinIO's own client, inside the MinIO container (credentials from its environment, never on a command line).
# shellcheck disable=SC2016  # expanded by the container's shell, on purpose
mc() { compose exec -T minio sh -c 'MC_HOST_drill="http://${MINIO_ROOT_USER}:${MINIO_ROOT_PASSWORD}@localhost:9000" mc "$@"' mc "$@"; }
# Every archived WAL and history object with its size and ETag (content hash), sorted.
wal_objects() { mc ls --json "drill/$bucket/custom-golf-stats/wal_005/" | grep -oE '"key":"[^"]+"|"size":[0-9]+|"etag":"[^"]+"' | paste -d' ' - - - | sort; }

# Waits until the server has archived the segment holding everything written so far.
wait_archived() {
  local segment archiver
  segment="$(src_sql -c "select pg_walfile_name(pg_switch_wal())")"
  for _ in $(seq 1 120); do
    if [[ "$(src_sql -c "select coalesce(last_archived_wal >= '$segment', false) from pg_stat_archiver")" == t ]]; then break; fi
    sleep 1
  done
  archiver="$(src_sql -c "select archived_count, failed_count, last_archived_wal from pg_stat_archiver")"
  [[ "$(src_sql -c "select coalesce(last_archived_wal >= '$segment', false) from pg_stat_archiver")" == t ]] \
    || fail "segment $segment was not archived within 120s (archived|failed|last: $archiver)"
  echo "archiver (archived|failed|last): $archiver"
}

backup_names() {
  compose exec -T pg-backup wal-g backup-list --json 2>/dev/null \
    | grep -oE '"backup_name": ?"[^"]+"' | sed -E 's/.*"([^"]+)"$/\1/' || true
}

# Starts a throwaway Postgres (no network) on a restored volume and prints the snapshot plus its recovery state.
verify_volume() {
  local name="$1" volume="$2"
  docker run -d --name "$name" --network none -v "$volume:/var/lib/postgresql/data" "$image" \
    postgres -c archive_mode=off >/dev/null
  for _ in $(seq 1 60); do
    if docker exec "$name" pg_isready -q -U "$db_user" -d "$db" 2>/dev/null; then break; fi
    sleep 1
  done
  docker exec "$name" pg_isready -q -U "$db_user" -d "$db" || fail "restored Postgres in $volume did not start"
  docker exec "$name" psql -X -q -At -v ON_ERROR_STOP=1 -U "$db_user" -d "$db" -c "$snapshot_sql"
  docker exec "$name" psql -X -q -At -v ON_ERROR_STOP=1 -U "$db_user" -d "$db" -c "select 'in_recovery', pg_is_in_recovery()"
  docker exec "$name" psql -X -q -At -v ON_ERROR_STOP=1 -U "$db_user" -d "$db" \
    -c "select 'timeline', timeline_id from pg_control_checkpoint()"
  docker exec "$name" psql -X -q -At -v ON_ERROR_STOP=1 -U "$db_user" -d "$db" \
    -c "select 'phases', string_agg(phase, ',' order by id) from drill_events"
}

step "Building the Postgres + WAL-G image (deploy/postgres)"
compose build --quiet postgres

step "Starting MinIO and creating bucket $bucket"
compose up -d --wait minio
mc mb --quiet "drill/$bucket" >/dev/null

step "Starting postgres (archiving to MinIO) and pg-backup"
compose up -d --wait postgres pg-backup

step "Waiting for pg-backup's first base backup"
for _ in $(seq 1 120); do
  if compose logs --no-color pg-backup 2>/dev/null | grep -q "base backup finished"; then break; fi
  if compose logs --no-color pg-backup 2>/dev/null | grep -q "base backup FAILED"; then fail "pg-backup's first base backup failed"; fi
  sleep 1
done
compose logs --no-color pg-backup | grep -q "base backup finished" || fail "pg-backup took no base backup within 120s"
first_backup="$(backup_names)"
[[ -n "$first_backup" ]] || fail "no base backup listed in storage"
echo "first backup: $first_backup"

step "Writing data before the base backup (batch A)"
src_sql >/dev/null <<'SQL'
create table drill_rounds (
  id bigserial primary key,
  player text not null,
  played_on date not null,
  score int not null,
  notes text
);
create table drill_events (id bigserial primary key, phase text not null);
insert into drill_rounds (player, played_on, score, notes)
select 'player-' || (g % 50), date '2020-01-01' + (g % 2000), 60 + (g % 40), repeat(md5(g::text), 4)
from generate_series(1, 50000) g;
insert into drill_events (phase) values ('A');
SQL

step "Taking a base backup with backup-now (WALG_RETAIN_FULL=1)"
compose exec -T pg-backup backup-now
backups="$(backup_names)"
[[ -n "$backups" ]] || fail "no base backup listed in storage after backup-now"
[[ "$(wc -l <<<"$backups" | tr -d ' ')" == 1 ]] || fail "expected exactly 1 base backup after retention, found: $backups"
[[ "$backups" != "$first_backup" ]] || fail "retention kept the old backup instead of the new one"
echo "backups after retention: $backups"

step "Writing more data after the base backup (batch B: WAL only)"
src_sql >/dev/null <<'SQL'
insert into drill_rounds (player, played_on, score, notes)
select 'player-' || (g % 50), date '2022-01-01' + (g % 900), 58 + (g % 45), repeat(md5((g * 7)::text), 6)
from generate_series(1, 150000) g;
update drill_rounds set score = score + 1 where id % 10 = 0;
delete from drill_rounds where id % 97 = 0;
insert into drill_events (phase) values ('B');
SQL
expected_b="$(src_sql -c "$snapshot_sql")"

sleep 1
target_time="$(src_sql -c "select clock_timestamp()")"
sleep 1

step "Writing the last batch after the point in time T = $target_time (batch C)"
src_sql >/dev/null <<'SQL'
insert into drill_rounds (player, played_on, score, notes)
select 'late-' || g, date '2026-11-18', 70, 'after T' from generate_series(1, 1000) g;
insert into drill_events (phase) values ('C');
SQL
expected_c="$(src_sql -c "$snapshot_sql")"

step "Waiting until the last WAL segment is archived"
wait_archived

step "Destroying the source database (container and data volume)"
compose rm --stop --force pg-backup postgres >/dev/null
docker volume rm "${project}_pgdata" >/dev/null
if docker volume inspect "${project}_pgdata" >/dev/null 2>&1; then fail "source volume still exists"; fi

step "Restoring the latest point into fresh volume $vol_latest"
"$backup_dir/restore.sh" "$vol_latest"
actual_latest="$(verify_volume "${project}-verify-latest" "$vol_latest")"
docker rm -f "${project}-verify-latest" >/dev/null

step "Putting the restored volume into service (runbook: new pgdata, copy, start with archiving on)"
compose up --no-start postgres 2>/dev/null
docker run --rm -v "$vol_latest:/from:ro" -v "${project}_pgdata:/to" "$image" \
  sh -c 'find /to -mindepth 1 -delete && cp -a /from/. /to/'
compose up -d --wait postgres
for _ in $(seq 1 60); do
  if wal_objects | grep -q '"key":"00000002.history'; then break; fi
  sleep 1
done
wal_objects | grep -q '"key":"00000002.history' || fail "timeline 2's history file never reached the storage"
echo "00000002.history is in storage"
src_sql >/dev/null -c "insert into drill_events (phase) values ('D on timeline 2')"
wait_archived
wal_objects | grep -q '"key":"00000002000' || fail "no timeline 2 WAL segment in storage"
wal_objects | grep -q '"key":"00000003' && fail "timeline 3 exists in storage before the second restore"
storage_before="$(wal_objects)"

step "Restoring the timeline 1 base backup ($backups) up to T into fresh volume $vol_pitr, timeline 2 still live"
"$backup_dir/restore.sh" --backup "$backups" --target-time "$target_time" "$vol_pitr"
actual_pitr="$(verify_volume "${project}-verify-pitr" "$vol_pitr")"
[[ "$(wal_objects)" == "$storage_before" ]] || fail "the restore changed the archived WAL in storage"

step "Comparing"
expect_latest="$expected_c
in_recovery|f
timeline|2
phases|A,B,C"
expect_pitr="$expected_b
in_recovery|f
timeline|3
phases|A,B"
echo "source at the end:        $(tr '\n' ' ' <<<"$expected_c")"
echo "restored (latest):        $(tr '\n' ' ' <<<"$actual_latest")"
echo "source at T:              $(tr '\n' ' ' <<<"$expected_b")"
echo "restored (up to T):       $(tr '\n' ' ' <<<"$actual_pitr")"
[[ "$actual_latest" == "$expect_latest" ]] || fail "latest restore differs from the source"
[[ "$actual_pitr" == "$expect_pitr" ]] || fail "point-in-time restore differs from the source at T (or did not pick the unused timeline 3)"
[[ "$expected_b" != "$expected_c" ]] || fail "batch C changed nothing, so the point-in-time check proves nothing"
echo "storage unchanged by the second restore ($(wc -l <<<"$storage_before" | tr -d ' ') WAL/history objects)"
