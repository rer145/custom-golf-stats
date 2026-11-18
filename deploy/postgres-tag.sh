#!/usr/bin/env bash
# Prints the content tag of the Postgres + WAL-G image: pg-<12 hex>, a sha256 over the paths and contents of the files
# in deploy/postgres (its whole build context), so it changes only when that directory does. No git needed: it gives
# the same answer in a checkout, in CI and in the deploy/ files copied to the server.
#   deploy/postgres-tag.sh            # e.g. pg-3f1c0a9b2d4e
# images.yml publishes the image under this tag; deploy.sh, remote-deploy.sh, restore.sh, smoke.sh and drill.sh export
# it as POSTGRES_CONTENT_TAG, which compose.yaml uses unless POSTGRES_TAG pins another tag.
# POSTGRES_CONTEXT overrides the directory hashed (used by the local test of remote-deploy.sh).
set -euo pipefail

dir="${POSTGRES_CONTEXT:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/postgres}"
[[ -d "$dir" ]] || { echo "postgres-tag.sh: no directory $dir" >&2; exit 1; }
cd "$dir"

sha256() { if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi; }

# One "<sha256 of the contents> <path>" line per file, in a fixed order, hashed again. Finder's .DS_Store is skipped
# so a look at the directory on a Mac doesn't change the tag.
sum="$(find . -type f ! -name .DS_Store | LC_ALL=C sort | while IFS= read -r file; do
  printf '%s %s\n' "$(sha256 <"$file" | cut -c1-64)" "$file"
done | sha256 | cut -c1-12)"
[[ "$sum" =~ ^[0-9a-f]{12}$ ]] || { echo "postgres-tag.sh: could not hash $dir" >&2; exit 1; }
echo "pg-$sum"
