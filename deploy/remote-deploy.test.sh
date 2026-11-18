#!/usr/bin/env bash
# Tests remote-deploy.sh's bookkeeping (current/previous tag, going back after a failed deploy, rollback guards, GHCR
# login over stdin, detached runs) with a fake deploy.sh and a fake docker, deploy.sh's SKIP_PULL mode with the fake
# docker, and the Postgres content tag (postgres-tag.sh) every compose call gets, so no Docker daemon or server is needed.
# Needs bash and GNU coreutils/util-linux (flock, setsid, sha256sum, tail --pid): any Linux; on macOS run it in a container:
#   deploy/remote-deploy.test.sh
#   docker run --rm -v "$PWD/deploy:/d:ro" ubuntu:24.04 /d/remote-deploy.test.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

mkdir -p "$work/deploy" "$work/bin"
cp "$here/remote-deploy.sh" "$here/postgres-tag.sh" "$work/deploy/"
mkdir -p "$work/real"
cp "$here/deploy.sh" "$here/postgres-tag.sh" "$work/real/"
# A stand-in for deploy/postgres (the real directory is hashed by its own test below).
mkdir -p "$work/deploy/postgres" "$work/real/postgres"
printf 'FROM postgres:17\n' | tee "$work/deploy/postgres/Dockerfile" >"$work/real/postgres/Dockerfile"
printf 'site {}\n' >"$work/deploy/Caddyfile"
calls="$work/calls"

# Fake deploy.sh: logs its tag (and "no-pull" under SKIP_PULL=1) and fails for the tags listed in $FAIL_TAGS.
cat >"$work/deploy/deploy.sh" <<'EOF'
#!/usr/bin/env bash
if [[ "${SKIP_PULL:-}" == "1" ]]; then echo "deploy.sh $1 no-pull" >>"$CALLS"; else echo "deploy.sh $1" >>"$CALLS"; fi
sleep "${DEPLOY_SLEEP:-0}"
for t in ${FAIL_TAGS:-}; do [[ "$t" == "$1" ]] && { echo "simulated failure of $1" >&2; exit 1; }; done
exit 0
EOF
# Fake docker: logs its arguments; `ps` answers $RUNNING_IMAGE; `login` records what came on stdin.
cat >"$work/bin/docker" <<'EOF'
#!/usr/bin/env bash
echo "docker $* IMAGE_TAG=${IMAGE_TAG:-} PG=${POSTGRES_CONTENT_TAG:-}" >>"$CALLS"
case "$1" in
  ps) [[ -n "${RUNNING_IMAGE:-}" ]] && echo "$RUNNING_IMAGE"; exit 0 ;;
  login) cat >"$(dirname "$CALLS")/login-stdin" ;;
esac
exit 0
EOF
chmod +x "$work/deploy/"*.sh "$work/bin/docker"

export CALLS="$calls" CGS_STATE_DIR="$work/state" PATH="$work/bin:$PATH"
rd="$work/deploy/remote-deploy.sh"
failures=0

# run <expected exit> <args...>: runs remote-deploy.sh with a fresh call log.
run() {
  local expected="$1" status=0
  shift
  : >"$calls"
  "$rd" "$@" >"$work/out" 2>&1 || status=$?
  if [[ "$status" != "$expected" ]]; then
    fail "remote-deploy.sh $* exited $status, expected $expected: $(cat "$work/out")"
  fi
}
fail() { echo "FAIL: $*" >&2; failures=$((failures + 1)); }
state() { cat "$CGS_STATE_DIR/$1" 2>/dev/null || true; }
expect_state() { [[ "$(state current-tag)" == "$1" && "$(state previous-tag)" == "$2" ]] \
  || fail "$3: current '$(state current-tag)' previous '$(state previous-tag)', expected '$1' '$2'"; }
expect_calls() { local got; got="$( (grep '^deploy.sh' "$calls" || true) | tr '\n' ' ' | sed 's/ $//')"
  [[ "$got" == "$1" ]] || fail "$2: deploy.sh calls '$got', expected '$1'"; }
t() { echo "--- $*"; }

t "a failing first deploy has nothing to go back to"
FAIL_TAGS="a" run 1 deploy a
expect_calls "deploy.sh a" "first deploy failing"
expect_state "" "" "first deploy failing"

t "first deploy"
run 0 deploy a
expect_calls "deploy.sh a" "first deploy"
expect_state a "" "first deploy"

t "second deploy records the previous tag"
run 0 deploy b
expect_calls "deploy.sh b" "second deploy"
expect_state b a "second deploy"

t "redeploying the current tag keeps previous"
run 0 deploy b
expect_calls "deploy.sh b" "redeploy"
expect_state b a "redeploy"

t "a failed deploy goes back to the current tag and exits 1"
FAIL_TAGS="c" run 1 deploy c
expect_calls "deploy.sh c deploy.sh b no-pull" "failed deploy"
expect_state b a "failed deploy"
grep -q "deploy of c FAILED" "$CGS_STATE_DIR/history.log" || fail "history lacks the failed deploy"

t "a failed deploy whose restore fails too exits 3"
FAIL_TAGS="c b" run 3 deploy c
expect_calls "deploy.sh c deploy.sh b no-pull" "double failure"

t "rollback refuses when the expected tag is not current"
run 1 rollback z
expect_calls "" "rollback guard"
expect_state b a "rollback guard"

t "rollback goes to previous and clears it"
run 0 rollback b
expect_calls "deploy.sh a no-pull" "rollback"
expect_state a "" "rollback"

t "a second rollback has no target"
run 1 rollback
expect_calls "" "second rollback"

t "a failed rollback exits 3"
run 0 deploy b
FAIL_TAGS="a" run 3 rollback b
expect_calls "deploy.sh a no-pull" "failed rollback"

t "deploy --no-pull uses the images on disk"
run 0 deploy --no-pull a
expect_calls "deploy.sh a no-pull" "deploy --no-pull"
expect_state a b "deploy --no-pull"
run 0 deploy b
run 2 deploy --no-pull
run 2 deploy a b

t "invalid tags are refused before anything runs"
run 2 deploy 'x;rm -rf ~'
run 2 deploy '-x'
# shellcheck disable=SC2016 # a literal command substitution, which must be refused as a tag
run 2 rollback '$(id)'
run 2 deploy
expect_calls "" "invalid tags"

t "without recorded state the running api container's tag is current"
rm -rf "$CGS_STATE_DIR"
RUNNING_IMAGE="ghcr.io/rer145/custom-golf-stats-api:old1" FAIL_TAGS="new1" run 1 deploy new1
expect_calls "deploy.sh new1 deploy.sh old1 no-pull" "fallback to running container"
expect_state old1 "" "fallback to running container"

t "a changed Caddyfile recreates caddy, an unchanged one does not"
run 0 deploy new2
grep -q "caddy" "$calls" && fail "caddy recreated with an unchanged Caddyfile"
printf 'site { respond 200 }\n' >"$work/deploy/Caddyfile"
run 0 deploy new3
grep -q "^docker compose up -d --wait --force-recreate --no-deps caddy IMAGE_TAG=new3 PG=pg-[0-9a-f]\{12\}$" "$calls" \
  || fail "caddy not recreated after a Caddyfile change: $(cat "$calls")"

t "compose runs with the current tag"
run 0 compose ps
grep -q "^docker compose ps IMAGE_TAG=new3 PG=$("$work/deploy/postgres-tag.sh")$" "$calls" || fail "compose wrapper: $(cat "$calls")"

t "login takes the token on stdin, never in arguments"
: >"$calls"
printf 'someone\nsecret-token-123\n' | "$rd" login >"$work/out" 2>&1 || fail "login failed: $(cat "$work/out")"
grep -q "secret-token-123" "$calls" && fail "the token appeared in docker's arguments"
[[ "$(cat "$work/login-stdin")" == "secret-token-123" ]] || fail "docker login did not get the token on stdin"
grep -q "^docker login ghcr.io --username someone --password-stdin" "$calls" || fail "login arguments: $(cat "$calls")"
run 2 login </dev/null

t "current prints the current tag"
[[ "$("$rd" current)" == "new3" ]] || fail "current printed '$("$rd" current)'"

t "start runs detached; wait streams the output and returns the exit code"
id="$("$rd" start deploy new4)"
[[ "$id" =~ ^[0-9A-Za-z-]+$ ]] || fail "start printed '$id'"
"$rd" wait "$id" >"$work/wait.out" 2>&1 || fail "wait for a good deploy failed: $(cat "$work/wait.out")"
grep -q "Deploying new4" "$work/wait.out" || fail "wait did not print the run's output: $(cat "$work/wait.out")"
[[ "$(state current-tag)" == "new4" ]] || fail "detached deploy did not record new4"
"$rd" wait "$id" >/dev/null 2>&1 || fail "a repeated wait failed"
id="$(FAIL_TAGS=bad DEPLOY_SLEEP=1 "$rd" start deploy bad)"
status=0; "$rd" wait "$id" >"$work/wait.out" 2>&1 || status=$?
[[ "$status" == "1" ]] || fail "wait for a failed deploy exited $status: $(cat "$work/wait.out")"
grep -q "Restored new4" "$work/wait.out" || fail "wait output lacks the restore: $(cat "$work/wait.out")"
run 2 start status
run 2 wait 'x;y'
run 2 wait nosuchrun

t "deploy.sh pulls by default, and with SKIP_PULL=1 only pulls missing images"
: >"$calls"
COMPOSE_FILE=/dev/null "$work/real/deploy.sh" t1 >/dev/null || fail "deploy.sh t1 failed"
grep -q "^docker compose pull --quiet --ignore-buildable IMAGE_TAG=t1 PG=pg-[0-9a-f]\{12\}$" "$calls" || fail "default deploy did not pull: $(cat "$calls")"
grep -q -- "--pull" "$calls" && fail "default deploy passed --pull: $(cat "$calls")"
: >"$calls"
SKIP_PULL=1 "$work/real/deploy.sh" t2 >/dev/null || fail "SKIP_PULL deploy.sh failed"
grep -q "compose pull\|compose --profile migrate pull" "$calls" && fail "SKIP_PULL=1 still pulled: $(cat "$calls")"
[[ "$(grep -c -- "--pull missing" "$calls")" == "3" ]] || fail "SKIP_PULL=1 calls: $(cat "$calls")"

t "postgres-tag.sh: pg-<12 hex>, stable, and changed only by deploy/postgres"
pg="$work/real/postgres-tag.sh"
tag1="$("$pg")"
[[ "$tag1" =~ ^pg-[0-9a-f]{12}$ ]] || fail "postgres-tag.sh printed '$tag1'"
[[ "$("$pg")" == "$tag1" ]] || fail "postgres-tag.sh is not stable"
[[ "$(cd / && "$pg")" == "$tag1" ]] || fail "postgres-tag.sh depends on the working directory"
grep -q "PG=$tag1$" "$calls" || fail "deploy.sh did not export POSTGRES_CONTENT_TAG=$tag1: $(cat "$calls")"
printf 'site {}\n' >"$work/real/Caddyfile"
[[ "$("$pg")" == "$tag1" ]] || fail "a file outside deploy/postgres changed the tag"
printf '#!/bin/sh\n' >"$work/real/postgres/new.sh"
tag2="$("$pg")"
[[ "$tag2" != "$tag1" ]] || fail "adding a file to deploy/postgres kept the tag"
mv "$work/real/postgres/new.sh" "$work/real/postgres/renamed.sh"
[[ "$("$pg")" != "$tag2" ]] || fail "renaming a file in deploy/postgres kept the tag"
rm "$work/real/postgres/renamed.sh"
[[ "$("$pg")" == "$tag1" ]] || fail "restoring deploy/postgres did not restore the tag"
printf 'FROM postgres:18\n' >"$work/real/postgres/Dockerfile"
[[ "$("$pg")" != "$tag1" ]] || fail "changing the Dockerfile kept the tag"
[[ "$("$here/postgres-tag.sh")" =~ ^pg-[0-9a-f]{12}$ ]] || fail "postgres-tag.sh on the real deploy/postgres: $("$here/postgres-tag.sh")"

t "the state directory is private"
[[ "$(stat -c %a "$CGS_STATE_DIR")" == "700" ]] || fail "state directory mode $(stat -c %a "$CGS_STATE_DIR")"

if [[ $failures -gt 0 ]]; then
  echo "$failures failure(s)" >&2
  exit 1
fi
echo "remote-deploy.sh: all tests passed"
