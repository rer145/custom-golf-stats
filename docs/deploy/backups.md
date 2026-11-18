# Database backups and restores (WAL-G)

Production Postgres is backed up by [WAL-G](https://github.com/wal-g/wal-g) to S3-compatible object storage:
Backblaze B2 or AWS S3. There are two parts:
- **Continuous WAL archiving.** Postgres pushes every completed write-ahead log segment as it fills. On a quiet database
  it also pushes one at least every 5 minutes (`archive_timeout`), so at most about 5 minutes of committed work can be
  lost.
- **Nightly full base backups.** The `pg-backup` sidecar takes one at `BACKUP_TIME` (UTC). After each backup it keeps
  the newest `WALG_RETAIN_FULL` (7) backups and the WAL that follows the oldest of them. A restore can reach any point
  from about a week ago up to the last archived segment.

The pieces:

| What | Where |
| --- | --- |
| Postgres 17.11 + WAL-G image (WAL-G v3.0.9, checksum-verified) | `deploy/postgres/Dockerfile`, published as `ghcr.io/rer145/custom-golf-stats-postgres:<sha>` |
| Services `postgres`, `pg-backup`, `restore` (profile) | `deploy/compose.yaml` |
| Settings | the "Backups" section of `deploy/.env` (see `deploy/.env.example`) |
| Restore into a new volume | `deploy/backup/restore.sh` |
| Drill: archive, back up, destroy, restore, compare | `deploy/backup/drill.sh`; in CI, `.github/workflows/backup.yml` |

Postgres stays off the internet. It serves the app on the internal `backend` network. Its second network, `backup`,
exists only so WAL-G can reach the storage. It publishes no ports. No credentials are in the image. WAL-G and the
scripts read them from the container environment and never print them.

The Postgres image is deployed like the API and web images:
- **Same tag as the rest.** It is `<POSTGRES_IMAGE>:<IMAGE_TAG>`, tagged with the same commit SHA. A deploy or a
  rollback moves the whole stack together, and changes to `deploy/postgres` ship with ordinary deploys.
- **Brief restarts.** Every commit rebuilds the image, so a deploy usually recreates `postgres` and `pg-backup`.
  `deploy.sh` does that first, before the migrations, so the database is unavailable for a few seconds. The running
  API reconnects.

Run the server commands below as `deploy` from `~/custom-golf-stats/deploy`. `./remote-deploy.sh compose` runs
`docker compose` with the deployed tag set.

## 1. Set up the storage

Use one bucket, or one prefix in a bucket, for this database alone. Don't share it with another server or a test stack.

### The key, and what it can do

One key serves the whole stack. Postgres needs it to upload WAL, and `pg-backup` needs it to delete old backups, so the
key can **delete**. Anyone who gets the server's `.env` could therefore delete the backups too.

You could give `postgres` a key without delete rights and keep the delete-capable key for `pg-backup` alone. WAL-G
reads the key from each container's environment, so that split is possible. The stack doesn't do it, and it's untested.

The protection used here is in the storage itself: deleted files are kept for a few days. The lifecycle settings
below do that. A stolen key, or a bug, can then only hide backups, and you have a week to notice and get them back.

### Backblaze B2

1. **Create the bucket.** Go to Buckets → Create a Bucket. Make it Private, e.g. `cgs-db-backups`. Server-side
   encryption (SSE-B2) is free, so turn it on. Leave Object Lock off: WAL-G's retention has to be able to delete old
   backups.
2. **Set the lifecycle.** Open the bucket's Lifecycle Settings and choose **Use custom lifecycle rules**: no file
   name prefix, "Hide files after" empty, and **"Delete files after they have been hidden for 7 days"**.
   - B2 never deletes a file outright: when WAL-G deletes a backup, B2 only hides it.
   - This rule removes hidden files after 7 days, so space is freed and you get a 7-day undo window.
   - "Keep only the last version of the file" also frees space, after 1 day, but gives almost no undo window.
3. **Create a key.** Go to Application Keys → Add a New Application Key:
   - Allow access to this bucket only.
   - Type of access: Read and Write.
   - Save the keyID and the applicationKey right away. The applicationKey is shown only once.
4. **Fill in `deploy/.env`.** Find the bucket's endpoint on the Buckets page, e.g. `s3.us-west-004.backblazeb2.com`. The
   region is the part after `s3.`:

   ```dotenv
   WALG_S3_PREFIX=s3://cgs-db-backups/custom-golf-stats
   AWS_ACCESS_KEY_ID=<keyID>
   AWS_SECRET_ACCESS_KEY=<applicationKey>
   AWS_ENDPOINT=https://s3.us-west-004.backblazeb2.com
   AWS_REGION=us-west-004
   AWS_S3_FORCE_PATH_STYLE=false
   ```

### AWS S3

1. **Create the bucket.** Create a bucket in the region you want, e.g. `cgs-db-backups` in `eu-west-1`. Keep Block
   Public Access on. Default encryption (SSE-S3) is fine. Turn **Versioning on**, so that a delete only adds a delete
   marker.
2. **Add a lifecycle rule** (Management → Create lifecycle rule) that applies to the whole bucket and has two actions:
   - **"Permanently delete noncurrent versions of objects"** after 7 days. Deleted backups stay recoverable for a
     week, then free their space.
   - **"Delete expired object delete markers or incomplete multipart uploads"**, with incomplete multipart uploads
     deleted after 1 day. A base backup interrupted halfway otherwise leaves parts behind that you pay for.
3. **Create a user for WAL-G.** Create an IAM user with no console access. Give it this inline policy, and create an
   access key for it. The policy has no `s3:DeleteObjectVersion`, so this key can't purge the old versions:

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       { "Effect": "Allow", "Action": "s3:ListBucket", "Resource": "arn:aws:s3:::cgs-db-backups" },
       {
         "Effect": "Allow",
         "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject", "s3:AbortMultipartUpload"],
         "Resource": "arn:aws:s3:::cgs-db-backups/*"
       }
     ]
   }
   ```

4. **Fill in `deploy/.env`:**

   ```dotenv
   WALG_S3_PREFIX=s3://cgs-db-backups/custom-golf-stats
   AWS_ACCESS_KEY_ID=<access key id>
   AWS_SECRET_ACCESS_KEY=<secret access key>
   AWS_ENDPOINT=https://s3.eu-west-1.amazonaws.com
   AWS_REGION=eu-west-1
   AWS_S3_FORCE_PATH_STYLE=false
   ```

### Image and schedule

- Leave `POSTGRES_IMAGE` as `ghcr.io/rer145/custom-golf-stats-postgres`. Its tag comes from `IMAGE_TAG`.
- The GHCR package needs the same access as the API and web packages. See "Registry access (GHCR)" in
  [README.md](README.md).
- `BACKUP_TIME` (default `03:00` UTC) and `WALG_RETAIN_FULL` (default `7`) are optional.
- `deploy/.env` holds the storage key, so keep it at mode 600.

## 2. First start and checks

The deploy starts `postgres` and `pg-backup` with the rest of the stack. If the storage has no base backup yet,
`pg-backup` takes one right away. After that it backs up nightly.

**The first backup:**

```sh
./remote-deploy.sh compose logs pg-backup
```

Look for `base backup finished`.

**The list of backups in storage:**

```sh
./remote-deploy.sh compose exec pg-backup wal-g backup-list
```

**The WAL archiver.** `failed_count` should stay at 0, and `last_archived_wal` should keep moving:

```sh
./remote-deploy.sh compose exec postgres sh -c \
  'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select * from pg_stat_archiver"'
```

**An extra base backup at any time.** This also applies retention:

```sh
./remote-deploy.sh compose exec pg-backup backup-now
```

### When the storage is down, or the key is wrong

**WAL piles up on the server's disk.** Postgres deletes a WAL segment only after it has been archived. Until then,
every segment stays in `pg_wal` on the server's disk, in the `pgdata` volume.

**How fast it grows** depends on what is written: at most one 16 MB segment per 5 minutes (`archive_timeout`) when
something was written, more under heavy writes.

- A quiet day can add up to about 4.5 GB.
- If the disk fills up, Postgres stops, and the site with it.

**Fix it before the disk fills.** Check with `df -h` and `docker system df -v`. Once the storage is reachable again,
the archiver pushes the backlog and Postgres frees the space by itself.

**What tells you it's failing:**

- `pg-backup` checks the archiver every 15 minutes (`ARCHIVE_CHECK_SECONDS`). While the last failure is newer than the
  last success, it logs a `WARNING: WAL archiving to the backup storage is FAILING` line. The line gives the failing
  segment and how many are waiting.
- A failed nightly backup is retried every hour, and logs `base backup FAILED`.
- Watch for both in `./remote-deploy.sh compose logs pg-backup`, or in whatever log alerting the server has.

## 3. Restoring

`deploy/backup/restore.sh` restores into a **new, empty Docker volume**. The steps are:
1. `wal-g backup-fetch` of a base backup;
2. archive recovery with `wal-g wal-fetch`, up to the latest archived WAL or to `--target-time`;
3. promotion and a clean shutdown.

It doesn't touch the running stack or its `pgdata` volume. It refuses a volume that isn't empty. It never writes to
the storage. So it's safe to run at any time, for example to check a backup.

It uses the Postgres image of the deployed tag (`remote-deploy.sh current`). Set `IMAGE_TAG` to use another tag.

```sh
backup/restore.sh custom-golf-stats_pgdata_restored                                         # latest point
backup/restore.sh --target-time "2026-11-18 14:30:00+00" custom-golf-stats_pgdata_restored  # point in time (UTC)
backup/restore.sh --backup base_000000010000000000000005 custom-golf-stats_pgdata_restored  # a given base backup
```

### Things to know

- **Choosing the base backup for a point in time.** The default base backup is `LATEST`. A point in time *before*
  that backup was taken can't be reached from it: recovery stops with "recovery ended before configured recovery
  target was reached". For such a point, pass an older base backup that finished before it, with `--backup`. Pick it
  from `./remote-deploy.sh compose exec pg-backup wal-g backup-list --detail`, by `finish_time`.
- **After a failed restore.** It leaves a partly written volume behind, and the next attempt refuses it. Remove it
  first with `docker volume rm <volume>`.
- **Timelines.** A restored database is promoted to a new timeline. Its history file is queued for archiving and
  reaches the storage once the volume runs with archiving on.
  - Later restores use it to pick a timeline number that isn't used yet, so they never overwrite the WAL of this one.
    The drill checks this.
  - A restored volume that you only inspect, with `archive_mode=off`, pushes nothing.

### Look inside a restored volume

Start a throwaway server with no network and no archiving, query it, then remove it:

```sh
img="$(./remote-deploy.sh compose config --images | grep custom-golf-stats-postgres | head -n 1)"
docker run -d --name pg-restored --network none \
  -v custom-golf-stats_pgdata_restored:/var/lib/postgresql/data "$img" postgres -c archive_mode=off
docker exec -it pg-restored psql -U custom_golf_stats -d custom_golf_stats
docker rm -f pg-restored
```

### Restoring production

Docker can't rename volumes, so the restored data is copied into `pgdata`. The old data is kept in its own volume first.

**1. Restore into a new volume.** The site keeps running meanwhile.

```sh
backup/restore.sh custom-golf-stats_pgdata_restored        # add --target-time "..." to undo a bad change
```

**2. Stop everything that uses the database.**

```sh
./remote-deploy.sh compose stop caddy web api pg-backup postgres
```

**3. Keep the current data, then swap in the restored copy.**

```sh
img="$(./remote-deploy.sh compose config --images | grep custom-golf-stats-postgres | head -n 1)"
docker run --rm -v custom-golf-stats_pgdata:/from:ro -v custom-golf-stats_pgdata_before_restore:/to "$img" \
  cp -a /from/. /to/
docker run --rm -v custom-golf-stats_pgdata_restored:/from:ro -v custom-golf-stats_pgdata:/to "$img" \
  sh -c 'find /to -mindepth 1 -delete && cp -a /from/. /to/'
```

**4. Start the stack again on the deployed tag.** Postgres comes up with archiving on, and pushes the new timeline's
history file.

```sh
./remote-deploy.sh compose up -d --wait
```

**5. Take a new base backup.** Later restores of this timeline start from it.

```sh
./remote-deploy.sh compose exec pg-backup backup-now
```

**6. Clean up.** Once the site checks out, remove the leftover volumes:

```sh
docker volume rm custom-golf-stats_pgdata_restored custom-golf-stats_pgdata_before_restore
```

**Undoing a bad release.** Restore to a point in time before the release was deployed. Then deploy the last good tag,
as in "Rollback" in [README.md](README.md).

**On a new server.** The first deploy creates an empty database. It must not archive into the old server's prefix,
so turn backups off for that one deploy:

1. Set up the server as in [README.md](README.md), with a `deploy/.env` that uses the same storage settings, but with
   `WALG_S3_PREFIX=none`.
2. Run one deploy of the tag you want, so that its images are on disk and a tag is recorded.
3. Set `WALG_S3_PREFIX` back to the real prefix.
4. Run steps 1 to 5 above. Step 4 recreates `postgres` and `pg-backup` with the real prefix.

The image sets `WALG_PREVENT_WAL_OVERWRITE`, so a fresh database pointed at an existing prefix by mistake fails to
archive and doesn't overwrite. Still, don't rely on it.

The drill tests these same steps: a restore into a new volume, the copy into a fresh `pgdata`, a start with archiving
on, then another restore.

## 4. The drill

`deploy/backup/drill.sh` proves that a restore actually works. It uses the production services from
`deploy/compose.yaml`, plus a MinIO server from `deploy/backup/drill.compose.yaml`:

1. It builds the image and starts MinIO, `postgres` (archiving to MinIO) and `pg-backup`, which takes the first backup.
2. It writes data, then takes a base backup with `backup-now`. Retention is 1, so the first backup has to be deleted.
3. It writes more data, notes a time T, writes a last batch, and waits until that batch's WAL is archived.
4. It deletes the source container and its volume. Only the object storage is left.
5. It runs `restore.sh` into a fresh volume, to the latest point. Every table's row count and content checksum must
   match the source at the end. The restored server must be out of recovery and on timeline 2.
6. It puts that volume into service as the runbook does, with archiving on. Then it checks that `00000002.history`
   reaches the storage, and writes and archives more WAL on timeline 2.
7. While timeline 2 runs, it restores the older timeline-1 base backup up to T into a second fresh volume. The data
   must match the source at T, without the last batch. The restore must pick the unused timeline 3, and the archived
   WAL in storage must be byte-for-byte unchanged.
8. It tears everything down.

To run it locally, you need Docker with Compose v2 and openssl:

```sh
deploy/backup/drill.sh
DRILL_KEEP=1 deploy/backup/drill.sh   # keep everything afterwards to look around
```

**When it runs in CI.** The `Backup drill` workflow runs it:
- on every push and pull request that touches `deploy/postgres`, `deploy/backup` or `deploy/compose.yaml`;
- weekly.

**The MinIO image.** MinIO stopped publishing container images. So the drill uses the community build
`pgsty/minio`, pinned by digest. `DRILL_MINIO_IMAGE` overrides it.

## 5. Upgrades

**WAL-G.** In `deploy/postgres/Dockerfile`:
1. Bump `WALG_VERSION` and both checksums.
2. Take the checksums from the release's `.tar.gz.sha256` files, and compare them with `sha256sum` of the downloaded
   archives.
3. Run the drill.

The change then ships with the next deploy.

**Postgres minor versions.** Bump the `FROM` lines the same way.

**Postgres major versions.** A major upgrade, e.g. 18, needs `pg_upgrade` or a dump and restore. It also needs a new
`WALG_S3_PREFIX`, so that backups from the two versions never mix.

## Throwaway stacks

`WALG_S3_PREFIX=none` turns archiving and `pg-backup` off: WAL is discarded. `deploy/smoke.sh` uses it, because the
smoke test checks the app, not backups. In production it is only for the first deploy on a new server, before a
restore (section 3).
