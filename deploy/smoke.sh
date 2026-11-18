#!/usr/bin/env bash
# Local smoke test of the production stack (deploy/compose.yaml) built from this checkout.
#  1. Builds the images and deploys them with deploy.sh under its own project name, with Caddy's internal CA serving
#     https://localhost (web) and https://api.localhost (API) on unprivileged ports. deploy.sh migrates first and
#     waits until every service is healthy.
#  2. Through Caddy, verifying TLS against Caddy's root certificate: registers a user, logs in and calls /me; then signs
#     in on the web site and loads the profile page with the session cookie.
#  3. Deploys a new tag whose migration fails, and checks deploy.sh exits non-zero while the running api, web and caddy
#     containers stay the same, on the old image, healthy and serving; then redeploys the good tag (a no-op).
#  4. Tears everything down, volumes included, whatever happened.
# Needs Docker with Compose v2, curl, openssl and python3. Secrets are generated per run and never printed.
#   deploy/smoke.sh                 # SMOKE_HTTP_PORT / SMOKE_HTTPS_PORT override the ports (default 18080 / 18443)
#   SMOKE_KEEP=1 deploy/smoke.sh    # leave the stack running for a look around (tear down with the printed command)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
http_port="${SMOKE_HTTP_PORT:-18080}"
https_port="${SMOKE_HTTPS_PORT:-18443}"
project="cgs-smoke"
web="localhost"
api="api.localhost"

work="$(mktemp -d)"
chmod 700 "$work"

cat >"$work/.env" <<EOF
WEB_DOMAIN=$web
API_DOMAIN=$api
ACME_EMAIL=smoke@example.com
HTTP_PORT=$http_port
HTTPS_PORT=$https_port
IMAGE_TAG=smoke
API_IMAGE=cgs-smoke-api
WEB_IMAGE=cgs-smoke-web
ALLOWED_HOSTS=$web
POSTGRES_DB=custom_golf_stats
POSTGRES_USER=custom_golf_stats
POSTGRES_PASSWORD=$(openssl rand -hex 24)
SESSION_SECRET=$(openssl rand -hex 32)
PROXY_SUBNET=${SMOKE_PROXY_SUBNET:-172.30.250.0/24}
EOF

compose() {
  docker compose --project-name "$project" --project-directory "$here" --env-file "$work/.env" \
    -f "$here/compose.yaml" -f "$here/compose.build.yaml" "$@"
}

teardown() {
  local status=$?
  if [[ $status -ne 0 ]]; then
    echo "--- smoke test FAILED; recent logs:" >&2
    compose logs --no-color --tail=40 >&2 || true
  fi
  if [[ "${SMOKE_KEEP:-}" == "1" ]]; then
    echo "Stack left running. Tear down with:"
    echo "  docker compose --project-name $project --env-file $work/.env -f $here/compose.yaml -f $here/compose.build.yaml down -v"
  else
    compose down -v --remove-orphans >/dev/null 2>&1 || true
    rm -rf "$work"
  fi
  docker image rm cgs-smoke-api:smoke-broken cgs-smoke-web:smoke-broken cgs-smoke-api:smoke-broken-migrator >/dev/null 2>&1 || true
  exit $status
}
trap teardown EXIT

step() { echo "==> $*"; }
fail() { echo "FAIL: $*" >&2; exit 1; }

# deploy.sh with this project's settings (it reads the standard Compose environment variables).
deploy() {
  COMPOSE_PROJECT_NAME="$project" COMPOSE_ENV_FILES="$work/.env" \
    COMPOSE_FILE="$here/compose.yaml:$here/compose.build.yaml" "$here/deploy.sh" "$@"
}

step "Building images ($project)"
compose --profile migrate build

step "Deploying with deploy.sh"
deploy smoke

step "Checking the API and web run as non-root"
api_uid="$(compose exec -T api id -u)"
web_uid="$(compose exec -T web id -u)"
[[ "$api_uid" != "0" && "$web_uid" != "0" ]] || fail "a service runs as root (api uid $api_uid, web uid $web_uid)"
echo "    api uid $api_uid; web uid $web_uid"

step "Fetching Caddy's internal root certificate"
for _ in $(seq 1 30); do
  if compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt >"$work/root.crt" 2>/dev/null && [[ -s "$work/root.crt" ]]; then
    break
  fi
  sleep 1
done
[[ -s "$work/root.crt" ]] || fail "Caddy's root certificate never appeared"

# curl through Caddy with real TLS verification; *.localhost always resolves to this machine.
c() {
  curl --silent --show-error --cacert "$work/root.crt" \
    --resolve "$web:$https_port:127.0.0.1" --resolve "$api:$https_port:127.0.0.1" \
    --resolve "$web:$http_port:127.0.0.1" --resolve "$api:$http_port:127.0.0.1" "$@"
}
api_url="https://$api:$https_port"
web_url="https://$web:$https_port"

step "Waiting for the API through Caddy"
for _ in $(seq 1 30); do
  if [[ "$(c -o /dev/null -w '%{http_code}' "$api_url/health" 2>/dev/null || true)" == "200" ]]; then break; fi
  sleep 1
done
c --fail -o /dev/null "$api_url/health" || fail "$api_url/health is not healthy"
echo "    $api_url/health 200"

step "Checking HTTPS redirect and security headers"
redirect="$(c -o /dev/null -w '%{http_code} %{redirect_url}' "http://$web:$http_port/sign-in")"
[[ "$redirect" == 30[178]\ https://$web* ]] || fail "http is not redirected to https ($redirect)"
c -D "$work/headers" -o /dev/null "$web_url/sign-in"
for h in strict-transport-security content-security-policy x-content-type-options x-frame-options; do
  grep -qi "^$h:" "$work/headers" || fail "web response lacks $h"
done
if grep -qi '^server:' "$work/headers"; then fail "web response still advertises a Server header"; fi
c -H 'Accept-Encoding: zstd, gzip' -D "$work/enc" -o /dev/null "$api_url/openapi/v1.json"
grep -qiE '^content-encoding: (zstd|gzip)' "$work/enc" || fail "API responses are not compressed"
echo "    http -> https redirect, security headers and compression present"

email="smoke-$(openssl rand -hex 6)@example.com"
# Request bodies and the bearer header go through files, so no credential appears on a command line.
python3 - "$email" "$work" <<'PY'
import json, secrets, sys, urllib.parse
email, work = sys.argv[1], sys.argv[2]
password = "Smoke-" + secrets.token_hex(12) + "!"
with open(f"{work}/credentials.json", "w") as f:
    json.dump({"email": email, "password": password}, f)
with open(f"{work}/sign-in.form", "w") as f:
    f.write(urllib.parse.urlencode({"email": email, "password": password, "next": "/profile"}))
PY

step "Registering $email through Caddy"
status="$(c -o "$work/register.out" -w '%{http_code}' -H 'Content-Type: application/json' --data-binary "@$work/credentials.json" "$api_url/auth/register")"
[[ "$status" == "200" ]] || fail "register returned $status: $(cat "$work/register.out")"

step "Logging in"
status="$(c -o "$work/login.json" -w '%{http_code}' -H 'Content-Type: application/json' --data-binary "@$work/credentials.json" "$api_url/auth/login")"
[[ "$status" == "200" ]] || fail "login returned $status"
python3 -c 'import json,sys; print("Authorization: Bearer " + json.load(open(sys.argv[1]))["accessToken"])' "$work/login.json" >"$work/auth-header"

step "Calling /me"
status="$(c -o "$work/me.json" -w '%{http_code}' -H "@$work/auth-header" "$api_url/me")"
[[ "$status" == "200" ]] || fail "/me returned $status"
grep -q "\"$email\"" "$work/me.json" || fail "/me did not return $email"
echo "    /me 200 for $email"

step "Signing in on the web site"
c --fail -o /dev/null -c "$work/cookies" "$web_url/sign-in"
status="$(c -o /dev/null -b "$work/cookies" -c "$work/cookies" -w '%{http_code} %{redirect_url}' \
  -H "Origin: $web_url" -H 'Content-Type: application/x-www-form-urlencoded' --data-binary "@$work/sign-in.form" "$web_url/sign-in")"
[[ "$status" == "303 $web_url/profile" ]] || fail "web sign-in answered '$status'"
grep -q 'cgs_session' "$work/cookies" || fail "web sign-in set no session cookie"
status="$(c -o "$work/profile.html" -w '%{http_code}' -b "$work/cookies" "$web_url/profile")"
[[ "$status" == "200" ]] || fail "/profile returned $status"
grep -q "Signed in as $email" "$work/profile.html" || fail "/profile does not show the signed-in user"
echo "    web sign-in 303 -> /profile, profile shows $email"

step "Deploying a tag whose migration fails"
# The new tag has the same api/web images but a migrator that fails, as a broken migration would.
docker tag cgs-smoke-api:smoke cgs-smoke-api:smoke-broken
docker tag cgs-smoke-web:smoke cgs-smoke-web:smoke-broken
printf 'FROM cgs-smoke-api:smoke-migrator\nENTRYPOINT ["sh", "-c", "echo simulated migration failure >&2; exit 1"]\n' \
  | docker build --quiet -t cgs-smoke-api:smoke-broken-migrator - >/dev/null
before="$(compose ps -q api web caddy | sort)"
if deploy smoke-broken >"$work/broken.log" 2>&1; then
  fail "deploy.sh succeeded although the migration failed"
fi
grep -q "Migrations failed" "$work/broken.log" || fail "deploy.sh failed for another reason: $(tail -5 "$work/broken.log")"
after="$(compose ps -q api web caddy | sort)"
[[ "$before" == "$after" ]] || fail "api/web/caddy containers were replaced by the failed deploy"
api_image="$(docker inspect --format '{{.Config.Image}}' "$(compose ps -q api)")"
[[ "$api_image" == "cgs-smoke-api:smoke" ]] || fail "api now runs $api_image"
api_health="$(docker inspect --format '{{.State.Health.Status}}' "$(compose ps -q api)")"
[[ "$api_health" == "healthy" ]] || fail "api is $api_health after the failed deploy"
c --fail -o /dev/null "$api_url/health" || fail "API stopped serving after the failed deploy"
status="$(c -o /dev/null -w '%{http_code}' -H "@$work/auth-header" "$api_url/me")"
[[ "$status" == "200" ]] || fail "/me returned $status after the failed deploy"
echo "    deploy.sh exited non-zero; api/web/caddy untouched, api on $api_image and $api_health, /me 200 through Caddy"

step "Redeploying the good tag is a no-op"
deploy smoke >"$work/redeploy.log" 2>&1 || fail "redeploy failed: $(tail -5 "$work/redeploy.log")"
[[ "$(compose ps -q api web caddy | sort)" == "$before" ]] || fail "redeploying the same tag replaced containers"
echo "    same api/web/caddy containers after the redeploy"

step "Smoke test passed"
