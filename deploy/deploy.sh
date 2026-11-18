#!/usr/bin/env bash
# Deploys (or rolls back to) one image tag of the stack in compose.yaml:
#   deploy/deploy.sh <image tag>          # e.g. the git SHA pushed by .github/workflows/images.yml
# Order matters, so a failed migration never takes the running site down:
#   1. pull the images for <tag> (nothing running changes);
#   2. make sure Postgres is up and healthy (only Postgres is touched);
#   3. run the migrations bundle for <tag> as a one-off container; on failure exit non-zero right here, leaving
#      api, web and caddy running on their current images;
#   4. only then `up -d --wait`, which recreates api, web and caddy on <tag> and waits until they are healthy.
# Rerunning with the same tag is a no-op apart from the (idempotent) migration run.
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

echo "==> Pulling images for $IMAGE_TAG"
# Services with a build section (local builds, compose.build.yaml) use their local images instead.
docker compose pull --quiet --ignore-buildable
docker compose --profile migrate pull --quiet --ignore-buildable migrate

echo "==> Starting Postgres"
docker compose up -d --wait postgres

echo "==> Applying migrations ($IMAGE_TAG-migrator)"
if ! docker compose run --rm --no-deps migrate; then
  echo "Migrations failed; the running services were left untouched on their current images." >&2
  exit 1
fi

echo "==> Starting the stack on $IMAGE_TAG"
docker compose up -d --wait --remove-orphans

echo "==> Deployed $IMAGE_TAG"
