#!/usr/bin/env bash
# Deploys (or rolls back to) one image tag of the stack in compose.yaml:
#   deploy/deploy.sh <image tag>          # e.g. the git SHA pushed by .github/workflows/images.yml
# Order matters, so a failed migration never takes the running site down:
#   1. pull the images for <tag> (nothing running changes);
#   2. make sure Postgres is up and healthy (only Postgres is touched). Its image has a content tag (postgres-tag.sh,
#      exported as POSTGRES_CONTENT_TAG) that changes only with deploy/postgres, so this recreates Postgres only when
#      its image really changed, never just because <tag> is new; POSTGRES_TAG (environment or .env) pins another tag;
#   3. run the migrations bundle for <tag> as a one-off container; on failure exit non-zero right here, leaving
#      api, web and caddy running on their current images;
#   4. only then `up -d --wait`, which recreates api, web and caddy on <tag> and waits until they are healthy.
# Rerunning with the same tag is a no-op apart from the (idempotent) migration run.
# SKIP_PULL=1 skips step 1 and only pulls an image that is not on disk (`--pull missing`), so going back to a tag whose
# images are still on disk needs no registry access (remote-deploy.sh uses it for rollbacks and restores).
# Compose settings come from the usual environment variables when set (COMPOSE_PROJECT_NAME, COMPOSE_FILE,
# COMPOSE_ENV_FILES); otherwise compose.yaml and .env next to this script are used.
set -euo pipefail

tag="${1:-}"
if [[ -z "$tag" || "$tag" == -* ]]; then
  echo "usage: $0 <image tag>" >&2
  exit 2
fi

cd "$(dirname "${BASH_SOURCE[0]}")"
export IMAGE_TAG="$tag"
POSTGRES_CONTENT_TAG="$(./postgres-tag.sh)"
export POSTGRES_CONTENT_TAG

pull=()
if [[ "${SKIP_PULL:-}" == "1" ]]; then
  echo "==> Using the images on disk for $IMAGE_TAG (pulling only missing ones)"
  pull=(--pull missing)
else
  echo "==> Pulling images for $IMAGE_TAG"
  # Services with a build section (local builds, compose.build.yaml) use their local images instead.
  docker compose pull --quiet --ignore-buildable
  docker compose --profile migrate pull --quiet --ignore-buildable migrate
fi

echo "==> Starting Postgres (content tag $POSTGRES_CONTENT_TAG unless POSTGRES_TAG pins one; recreated only if it changed)"
docker compose up -d --wait ${pull[@]+"${pull[@]}"} postgres

echo "==> Applying migrations ($IMAGE_TAG-migrator)"
if ! docker compose run --rm --no-deps ${pull[@]+"${pull[@]}"} migrate; then
  echo "Migrations failed; the running services were left untouched on their current images." >&2
  exit 1
fi

echo "==> Starting the stack on $IMAGE_TAG"
docker compose up -d --wait --remove-orphans ${pull[@]+"${pull[@]}"}

echo "==> Deployed $IMAGE_TAG"
