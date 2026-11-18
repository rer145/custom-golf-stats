# Postgres 17 with WAL-G for continuous WAL archiving and base backups to S3-compatible storage (Backblaze B2, AWS S3,
# MinIO). Published by .github/workflows/images.yml as ghcr.io/rer145/custom-golf-stats-postgres:<sha>.
# The image holds no credentials: WAL-G reads WALG_S3_PREFIX and the AWS_* variables from the container environment
# (deploy/compose.yaml passes them from deploy/.env). See docs/deploy/backups.md.
#
# One image, three roles in deploy/compose.yaml:
#   postgres   the server; every completed WAL segment is pushed by archive_command (CMD below)
#   pg-backup  sidecar running backup-scheduler: a full base backup nightly, then retention (keep WALG_RETAIN_FULL)
#   restore    one-off (profile `restore`) running restore-pgdata; driven by deploy/backup/restore.sh

# Download and verify WAL-G on the build platform; only the verified binary reaches the final image.
FROM --platform=$BUILDPLATFORM postgres:17.11 AS walg
ARG TARGETARCH
# Bump deliberately: a new version needs both checksums, taken from the release's *.tar.gz.sha256 files and checked
# against a local `sha256sum` of the downloaded archives.
ARG WALG_VERSION=v3.0.9
ARG WALG_SHA256_AMD64=4f03ee4679db7f660bfd1b2b8291ac5df247cd2ec48074b60c112f4897299f24
ARG WALG_SHA256_ARM64=20191a37ad091a880c25f4cc907e6f0deb7f12bdd8a53fd62dbc7fcbf2eac712
ADD https://github.com/wal-g/wal-g/releases/download/${WALG_VERSION}/wal-g-pg-22.04-amd64.tar.gz /tmp/walg-amd64.tar.gz
ADD https://github.com/wal-g/wal-g/releases/download/${WALG_VERSION}/wal-g-pg-22.04-aarch64.tar.gz /tmp/walg-arm64.tar.gz
RUN set -eu; \
    case "$TARGETARCH" in \
      amd64) sum="$WALG_SHA256_AMD64"; name=wal-g-pg-22.04-amd64 ;; \
      arm64) sum="$WALG_SHA256_ARM64"; name=wal-g-pg-22.04-aarch64 ;; \
      *) echo "unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    echo "$sum  /tmp/walg-$TARGETARCH.tar.gz" | sha256sum -c -; \
    mkdir /tmp/walg; \
    tar -xzf "/tmp/walg-$TARGETARCH.tar.gz" -C /tmp/walg; \
    install -m 0755 "/tmp/walg/$name" /usr/local/bin/wal-g

FROM postgres:17.11 AS runtime
COPY --from=walg /usr/local/bin/wal-g /usr/local/bin/wal-g
COPY --chmod=0755 archive-wal.sh /usr/local/bin/archive-wal
COPY --chmod=0755 backup-now.sh /usr/local/bin/backup-now
COPY --chmod=0755 backup-scheduler.sh /usr/local/bin/backup-scheduler
COPY --chmod=0755 restore-pgdata.sh /usr/local/bin/restore-pgdata
RUN wal-g --version

# lz4 keeps archiving cheap on a small VPS; WAL segments of a quiet database compress to almost nothing.
ENV WALG_COMPRESSION_METHOD=lz4

# Archiving is part of the server command line rather than an initdb script, so it also applies to an existing data
# volume. archive_timeout bounds how much committed work can be lost (5 minutes) on a quiet database; a forced switch
# happens only when something was written since the last one. A failed push is retried by Postgres; the segment stays
# in pg_wal until it succeeds (watch pg_stat_archiver.failed_count). archive-wal is `wal-g wal-push`, plus an explicit
# off switch for throwaway test stacks (WALG_S3_PREFIX=none).
CMD ["postgres", \
     "-c", "archive_mode=on", \
     "-c", "archive_command=/usr/local/bin/archive-wal %p", \
     "-c", "archive_timeout=300"]
