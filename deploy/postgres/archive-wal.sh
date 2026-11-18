#!/bin/sh
# Postgres archive_command (see CMD in the Dockerfile): pushes one completed WAL segment with WAL-G.
# WALG_S3_PREFIX=none turns archiving off on purpose, for throwaway test stacks only (deploy/smoke.sh): the segment is
# then reported as archived and discarded. Never use it in production.
if [ "${WALG_S3_PREFIX:-}" = none ]; then
  exit 0
fi
exec /usr/local/bin/wal-g wal-push "$1"
