#!/usr/bin/env bash
# Server-side wrapper around deploy.sh that remembers which image tag is deployed, so a failed deploy can go back.
# .github/workflows/deploy.yml runs it over SSH; it works the same by hand on the server:
#   remote-deploy.sh deploy [--no-pull] <tag>
#                                       deploy <tag> with deploy.sh. On success <tag> becomes current and the tag it
#                                       replaced becomes previous. If deploy.sh fails, the tag that was current is
#                                       deployed again from the images on disk and the command exits 1.
#                                       --no-pull uses the images on disk (SKIP_PULL=1 for deploy.sh): no registry
#                                       access is needed when they are all there.
#   remote-deploy.sh rollback [<tag>]   deploy the previous tag again, from the images on disk, and make it current.
#                                       With <tag>, refuse unless <tag> is the current one (the workflow passes the tag
#                                       it just deployed, so it never rolls back someone else's deploy).
#   remote-deploy.sh start <command> [<args>...]
#                                       run deploy or rollback detached from this SSH session (it survives a dropped
#                                       connection) and print a run id; its output goes to $CGS_STATE_DIR/runs/<id>.log.
#   remote-deploy.sh wait <id>          print that run's output as it is written, then exit with its exit code. Safe to
#                                       repeat after a dropped connection (the output is printed again from the start).
#   remote-deploy.sh status             print the current and previous tags and the recent history.
#   remote-deploy.sh current            print the current tag (empty when there is none).
#   remote-deploy.sh compose <args...>  docker compose for this stack with IMAGE_TAG set to the current tag (and
#                                       POSTGRES_CONTENT_TAG from postgres-tag.sh), e.g. `remote-deploy.sh compose ps`
#                                       or `remote-deploy.sh compose logs api`.
#   remote-deploy.sh login              docker login ghcr.io; reads the user name and then the token, one per line,
#                                       from stdin (so the token never appears on a command line).
#   remote-deploy.sh logout             docker logout ghcr.io.
# Exit codes: 0 done; 1 the deploy or rollback failed but the stack is back on the tag it was on (or there was nothing
# to go back to); 2 usage; 3 the deploy failed AND putting the old tag back failed too: look at the server now.
#
# Rolling back puts the previous images back. It does not undo database migrations (EF Core migrations only ever
# move forward here) and it keeps the deploy/ files currently on the server, so Postgres stays on the image those files
# name (its content tag comes from deploy/postgres, see postgres-tag.sh) and is not restarted. To go back to an older
# commit's files (and its Postgres image) as well, run the Deploy workflow with that commit's SHA.
#
# State lives outside deploy/ (which the workflow overwrites), in $CGS_STATE_DIR (default ../state next to deploy/):
#   current-tag, previous-tag, caddyfile.sha256, history.log
# DEPLOY_SCRIPT overrides the deploy.sh that is run (used by the local test of this script).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
state="${CGS_STATE_DIR:-$here/../state}"
deploy_script="${DEPLOY_SCRIPT:-$here/deploy.sh}"
project="${COMPOSE_PROJECT_NAME:-custom-golf-stats}"
registry="ghcr.io"

usage() {
  sed -n '3,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

log() { echo "==> $*"; }
err() { echo "error: $*" >&2; }

valid_tag() { [[ "$1" =~ ^[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}$ ]]; }

read_state() { if [[ -s "$state/$1" ]]; then head -n 1 "$state/$1"; fi; }

write_state() {
  printf '%s\n' "$2" >"$state/$1.tmp"
  mv -f "$state/$1.tmp" "$state/$1"
}

history() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >>"$state/history.log"; }

# The tag deploy.sh last deployed; when nothing is recorded yet (a stack deployed before this script existed), the tag
# of the running api container.
current_tag() {
  local tag image
  tag="$(read_state current-tag)"
  if [[ -z "$tag" ]] && command -v docker >/dev/null 2>&1; then
    image="$(docker ps --filter "label=com.docker.compose.project=$project" \
      --filter "label=com.docker.compose.service=api" --format '{{.Image}}' 2>/dev/null | head -n 1 || true)"
    if [[ "$image" == *:* ]]; then tag="${image##*:}"; fi
  fi
  if [[ -n "$tag" ]] && valid_tag "$tag"; then printf '%s\n' "$tag"; fi
}

# The Postgres image's content tag for the deploy/ files on the server; deploy.sh sets it itself.
postgres_content_tag() { "$here/postgres-tag.sh"; }

lock() {
  mkdir -p "$state"
  chmod 700 "$state"
  if command -v flock >/dev/null 2>&1; then
    exec 9>"$state/lock"
    flock -n 9 || { err "another deploy is running on this server"; exit 1; }
  fi
}

# run_deploy <tag> [no-pull]
run_deploy() {
  if [[ "${2:-}" == "no-pull" ]]; then SKIP_PULL=1 "$deploy_script" "$1"; else SKIP_PULL="" "$deploy_script" "$1"; fi
}

# Caddy reads its Caddyfile through a bind mount, and the workflow replaces the file, so the running container keeps the
# old one: recreate Caddy whenever the file differs from the one last deployed.
refresh_caddy() {
  local tag="$1" file="$here/Caddyfile" sum old
  [[ -f "$file" ]] || return 0
  sum="$(sha256sum "$file" | cut -d' ' -f1)" || return 1
  old="$(read_state caddyfile.sha256)"
  if [[ -n "$old" && "$sum" != "$old" ]]; then
    log "Caddyfile changed; recreating caddy"
    (cd "$here" && IMAGE_TAG="$tag" POSTGRES_CONTENT_TAG="$(postgres_content_tag)" docker compose up -d --wait --force-recreate --no-deps caddy) || return 1
  fi
  write_state caddyfile.sha256 "$sum"
}

cmd_deploy() {
  local mode="" tag current
  if [[ "${1:-}" == "--no-pull" ]]; then mode="no-pull"; shift; fi
  [[ $# -eq 1 && -n "$1" ]] || usage
  tag="$1"
  valid_tag "$tag" || { err "not a valid image tag: $tag"; exit 2; }
  lock
  current="$(current_tag)"

  log "Deploying $tag (current: ${current:-none})"
  if run_deploy "$tag" "$mode" && refresh_caddy "$tag"; then
    if [[ "$tag" != "$current" ]]; then
      [[ -z "$current" ]] || write_state previous-tag "$current"
      write_state current-tag "$tag"
      history "deployed $tag (previous ${current:-none})"
    else
      # Same tag again: a no-op apart from the idempotent migration run; previous stays what it was.
      write_state current-tag "$tag"
      history "redeployed $tag"
    fi
    log "Current tag: $tag; previous: $(read_state previous-tag || true)"
    return 0
  fi

  history "deploy of $tag FAILED"
  err "deploying $tag failed"
  if [[ -z "$current" || "$current" == "$tag" ]]; then
    err "no earlier tag to go back to"
    exit 1
  fi
  log "Going back to $current"
  if run_deploy "$current" no-pull; then
    write_state current-tag "$current"
    history "restored $current after the failed deploy of $tag"
    log "Restored $current"
    exit 1
  fi
  history "restoring $current FAILED"
  err "restoring $current failed too: the stack needs attention (remote-deploy.sh compose ps)"
  exit 3
}

cmd_rollback() {
  local expected="${1:-}" current previous
  [[ -z "$expected" ]] || valid_tag "$expected" || { err "not a valid image tag: $expected"; exit 2; }
  lock
  current="$(current_tag)"
  previous="$(read_state previous-tag)"
  if [[ -n "$expected" && "$expected" != "$current" ]]; then
    err "refusing to roll back: the current tag is ${current:-unknown}, not $expected"
    exit 1
  fi
  if [[ -z "$previous" || "$previous" == "$current" ]]; then
    err "no previous tag recorded to roll back to; deploy an explicit older tag instead (remote-deploy.sh deploy <tag>)"
    exit 1
  fi

  log "Rolling back from ${current:-unknown} to $previous"
  if run_deploy "$previous" no-pull; then
    write_state current-tag "$previous"
    # The tag rolled away from is not offered as the next rollback target.
    rm -f "$state/previous-tag"
    history "rolled back from ${current:-unknown} to $previous"
    log "Current tag: $previous"
    return 0
  fi
  history "rollback from ${current:-unknown} to $previous FAILED"
  err "rolling back to $previous failed: the stack needs attention (remote-deploy.sh compose ps)"
  exit 3
}

cmd_status() {
  echo "current:  $(current_tag || true)"
  echo "previous: $(read_state previous-tag || true)"
  if [[ -s "$state/history.log" ]]; then
    echo "history (latest last):"
    tail -n 20 "$state/history.log" | sed 's/^/  /'
  fi
}

runs_dir() { mkdir -p "$state/runs"; chmod 700 "$state" "$state/runs"; echo "$state/runs"; }

# Runs remote-deploy.sh <command> in its own session with no terminal, so a dropped SSH connection neither hangs it up
# nor breaks its output; `wait` follows it.
cmd_start() {
  local runs id
  case "${1:-}" in deploy | rollback) ;; *) usage ;; esac
  runs="$(runs_dir)"
  id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
  : >"$runs/$id.log"
  setsid "${BASH_SOURCE[0]}" _run "$id" "$@" </dev/null >>"$runs/$id.log" 2>&1 &
  # Keep the last 20 runs.
  find "$runs" -maxdepth 1 -name '*.log' -printf '%f\n' | sort | head -n -20 | while read -r old; do
    rm -f "$runs/$old" "$runs/${old%.log}.pid" "$runs/${old%.log}.status"
  done
  echo "$id"
}

cmd_run() {
  local id="$1" runs status=0
  shift
  runs="$(runs_dir)"
  echo "$$" >"$runs/$id.pid"
  "${BASH_SOURCE[0]}" "$@" || status=$?
  echo "$status" >"$runs/$id.status.tmp"
  mv -f "$runs/$id.status.tmp" "$runs/$id.status"
}

cmd_wait() {
  local id="${1:-}" runs pid status
  [[ "$id" =~ ^[0-9A-Za-z-]+$ ]] || usage
  runs="$(runs_dir)"
  [[ -e "$runs/$id.log" ]] || { err "no run $id"; exit 2; }
  for _ in $(seq 1 60); do
    [[ -s "$runs/$id.pid" || -e "$runs/$id.status" ]] && break
    sleep 0.5
  done
  pid="$(cat "$runs/$id.pid" 2>/dev/null || true)"
  if [[ -n "$pid" && ! -e "$runs/$id.status" ]]; then
    tail -n +1 --pid="$pid" -f "$runs/$id.log"
  else
    cat "$runs/$id.log"
  fi
  for _ in $(seq 1 20); do
    [[ -e "$runs/$id.status" ]] && break
    sleep 0.5
  done
  status="$(cat "$runs/$id.status" 2>/dev/null || true)"
  [[ "$status" =~ ^[0-9]+$ ]] || { err "run $id ended without an exit status"; exit 3; }
  exit "$status"
}

cmd_compose() {
  local tag pg_tag
  tag="$(current_tag)"
  [[ -n "$tag" ]] || { err "no current tag recorded"; exit 1; }
  pg_tag="$(postgres_content_tag)"
  cd "$here"
  IMAGE_TAG="$tag" POSTGRES_CONTENT_TAG="$pg_tag" exec docker compose "$@"
}

cmd_login() {
  local user token
  IFS= read -r user || true
  IFS= read -r token || true
  [[ -n "$user" && -n "$token" ]] || { err "login expects the user name and the token on stdin, one per line"; exit 2; }
  printf '%s' "$token" | docker login "$registry" --username "$user" --password-stdin >/dev/null
  log "Logged in to $registry"
}

cmd_logout() { docker logout "$registry" >/dev/null 2>&1 || true; log "Logged out of $registry"; }

command="${1:-}"
[[ $# -gt 0 ]] && shift
case "$command" in
  deploy) cmd_deploy "$@" ;;
  rollback) [[ $# -le 1 ]] || usage; cmd_rollback "${1:-}" ;;
  start) cmd_start "$@" ;;
  _run) [[ $# -ge 2 ]] || usage; cmd_run "$@" ;;
  wait) [[ $# -eq 1 ]] || usage; cmd_wait "$1" ;;
  status) cmd_status ;;
  current) current_tag || true ;;
  compose) cmd_compose "$@" ;;
  login) cmd_login ;;
  logout) cmd_logout ;;
  *) usage ;;
esac
