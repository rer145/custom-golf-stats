#!/usr/bin/env bash
# Tests remote-deploy.sh's bookkeeping (current/previous tag, going back after a failed deploy, rollback guards, GHCR
# login over stdin) with a fake deploy.sh and a fake docker, so no Docker daemon or server is needed.
# Needs bash, flock and sha256sum (any Linux; on macOS run it in a container):
#   deploy/remote-deploy.test.sh
#   docker run --rm -v "$PWD/deploy:/d:ro" ubuntu:24.04 /d/remote-deploy.test.sh
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

mkdir -p "$work/deploy" "$work/bin"
cp "$here/remote-deploy.sh" "$work/deploy/"
printf 'site {}\n' >"$work/deploy/Caddyfile"
calls="$work/calls"

# Fake deploy.sh: logs its tag and fails for the tags listed in $FAIL_TAGS.
cat >"$work/deploy/deploy.sh" <<'EOF'
#!/usr/bin/env bash
echo "deploy.sh $1" >>"$CALLS"
for t in ${FAIL_TAGS:-}; do [[ "$t" == "$1" ]] && { echo "simulated failure of $1" >&2; exit 1; }; done
exit 0
EOF
# Fake docker: logs its arguments; `ps` answers $RUNNING_IMAGE; `login` records what came on stdin.
cat >"$work/bin/docker" <<'EOF'
#!/usr/bin/env bash
echo "docker $* IMAGE_TAG=${IMAGE_TAG:-}" >>"$CALLS"
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
expect_calls "deploy.sh c deploy.sh b" "failed deploy"
expect_state b a "failed deploy"
grep -q "deploy of c FAILED" "$CGS_STATE_DIR/history.log" || fail "history lacks the failed deploy"

t "a failed deploy whose restore fails too exits 3"
FAIL_TAGS="c b" run 3 deploy c
expect_calls "deploy.sh c deploy.sh b" "double failure"

t "rollback refuses when the expected tag is not current"
run 1 rollback z
expect_calls "" "rollback guard"
expect_state b a "rollback guard"

t "rollback goes to previous and clears it"
run 0 rollback b
expect_calls "deploy.sh a" "rollback"
expect_state a "" "rollback"

t "a second rollback has no target"
run 1 rollback
expect_calls "" "second rollback"

t "a failed rollback exits 3"
run 0 deploy b
FAIL_TAGS="a" run 3 rollback b
expect_calls "deploy.sh a" "failed rollback"

t "invalid tags are refused before anything runs"
run 2 deploy 'x;rm -rf ~'
run 2 deploy '-x'
run 2 rollback '$(id)'
run 2 deploy
expect_calls "" "invalid tags"

t "without recorded state the running api container's tag is current"
rm -rf "$CGS_STATE_DIR"
RUNNING_IMAGE="ghcr.io/rer145/custom-golf-stats-api:old1" FAIL_TAGS="new1" run 1 deploy new1
expect_calls "deploy.sh new1 deploy.sh old1" "fallback to running container"
expect_state old1 "" "fallback to running container"

t "a changed Caddyfile recreates caddy, an unchanged one does not"
run 0 deploy new2
grep -q "caddy" "$calls" && fail "caddy recreated with an unchanged Caddyfile"
printf 'site { respond 200 }\n' >"$work/deploy/Caddyfile"
run 0 deploy new3
grep -q "^docker compose up -d --wait --force-recreate --no-deps caddy IMAGE_TAG=new3$" "$calls" \
  || fail "caddy not recreated after a Caddyfile change: $(cat "$calls")"

t "compose runs with the current tag"
run 0 compose ps
grep -q "^docker compose ps IMAGE_TAG=new3$" "$calls" || fail "compose wrapper: $(cat "$calls")"

t "login takes the token on stdin, never in arguments"
: >"$calls"
printf 'someone\nsecret-token-123\n' | "$rd" login >"$work/out" 2>&1 || fail "login failed: $(cat "$work/out")"
grep -q "secret-token-123" "$calls" && fail "the token appeared in docker's arguments"
[[ "$(cat "$work/login-stdin")" == "secret-token-123" ]] || fail "docker login did not get the token on stdin"
grep -q "^docker login ghcr.io --username someone --password-stdin" "$calls" || fail "login arguments: $(cat "$calls")"
run 2 login </dev/null

t "the state directory is private"
[[ "$(stat -c %a "$CGS_STATE_DIR")" == "700" ]] || fail "state directory mode $(stat -c %a "$CGS_STATE_DIR")"

if [[ $failures -gt 0 ]]; then
  echo "$failures failure(s)" >&2
  exit 1
fi
echo "remote-deploy.sh: all tests passed"
