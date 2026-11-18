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

## 1. Set up the storage

Use one bucket, or one prefix in a bucket, for this database alone. Don't share it with another server or a test stack.

### Backblaze B2

1. **Create the bucket.** Go to Buckets → Create a Bucket. Make it Private, e.g. `cgs-db-backups`. Server-side
   encryption (SSE-B2) is free, so turn it on. Leave Object Lock off: WAL-G's retention has to be able to delete old
   backups.
2. **Set the lifecycle.** Open the bucket's Lifecycle Settings and choose **Keep only the last version of the file**.
   B2 keeps every version of every file. When WAL-G deletes a backup, B2 only hides it. Without this rule, hidden files
   stay forever and keep costing money.
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
   Public Access on and versioning off. Default encryption (SSE-S3) is fine.
2. **Create a user for WAL-G.** Create an IAM user with no console access. Give it this inline policy, and create an
   access key for it:

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       { "Effect": "Allow", "Action": "s3:ListBucket", "Resource": "arn:aws:s3:::cgs-db-backups" },
       {
         "Effect": "Allow",
         "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"],
         "Resource": "arn:aws:s3:::cgs-db-backups/*"
       }
     ]
   }
   ```

3. **Fill in `deploy/.env`:**

   ```dotenv
   WALG_S3_PREFIX=s3://cgs-db-backups/custom-golf-stats
   AWS_ACCESS_KEY_ID=<access key id>
   AWS_SECRET_ACCESS_KEY=<secret access key>
   AWS_ENDPOINT=https://s3.eu-west-1.amazonaws.com
   AWS_REGION=eu-west-1
   AWS_S3_FORCE_PATH_STYLE=false
   ```

### Image and schedule

- `POSTGRES_IMAGE` is the Postgres image built from `deploy/postgres` by `.github/workflows/images.yml`. Its tag is the
  SHA of a commit that changed `deploy/postgres`.
- It is pinned on its own, not through `IMAGE_TAG`. So app deploys never restart the database.
- `BACKUP_TIME` (default `03:00` UTC) and `WALG_RETAIN_FULL` (default `7`) are optional.

`deploy/.env` holds these keys, so keep it at mode 600.

## 2. First start and checks

`deploy/deploy.sh <tag>` starts `postgres` and `pg-backup` with the rest of the stack. If the storage has no base
backup yet, `pg-backup` takes one right away. After that it backs up nightly. Run these checks from `deploy/`.

**The first backup:**

```sh
docker compose logs pg-backup
```

Look for `base backup finished`.

**The list of backups in storage:**

```sh
docker compose exec pg-backup wal-g backup-list
```

**The WAL archiver.** `failed_count` should stay at 0, and `last_archived_wal` should keep moving:

```sh
docker compose exec postgres sh -c \
  'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select * from pg_stat_archiver"'
```

**An extra base backup at any time.** This also applies retention:

```sh
docker compose exec pg-backup backup-now
```

### When storage is down

The archiver keeps retrying, and segments wait in `pg_wal` on the server disk. Fix the storage or the keys soon.

A failed nightly backup is retried every hour. It shows as `base backup FAILED` in `docker compose logs pg-backup`.

## 3. Restoring

`deploy/backup/restore.sh` restores into a **new, empty Docker volume**. The steps are:
1. `wal-g backup-fetch` of a base backup;
2. archive recovery with `wal-g wal-fetch`, up to the latest archived WAL or to `--target-time`;
3. promotion and a clean shutdown.

It doesn't touch the running stack or its `pgdata` volume. It refuses a volume that isn't empty. It never writes to
the storage. So it's safe to run at any time, for example to check a backup.

Run it from `deploy/`:

```sh
backup/restore.sh custom-golf-stats_pgdata_restored                                         # latest point
backup/restore.sh --target-time "2026-11-18 14:30:00+00" custom-golf-stats_pgdata_restored  # point in time (UTC)
backup/restore.sh --backup base_000000010000000000000005 custom-golf-stats_pgdata_restored  # a given base backup
```

**Look inside a restored volume.** Start a throwaway server with no network and no archiving, query it, then remove it:

```sh
img="$(grep '^POSTGRES_IMAGE=' .env | cut -d= -f2-)"
docker run -d --name pg-restored --network none \
  -v custom-golf-stats_pgdata_restored:/var/lib/postgresql/data "$img" postgres -c archive_mode=off
docker exec -it pg-restored psql -U custom_golf_stats -d custom_golf_stats
docker rm -f pg-restored
```

### Restoring production

Docker can't rename volumes, so the restored data is copied into `pgdata`. The old data is kept in its own volume first.
Run these steps from `deploy/` on the server.

**1. Restore into a new volume.** The site keeps running meanwhile.

```sh
backup/restore.sh custom-golf-stats_pgdata_restored        # add --target-time "..." to undo a bad change
```

**2. Stop everything that uses the database.**

```sh
docker compose stop caddy web api pg-backup postgres
```

**3. Keep the current data, then swap in the restored copy.**

```sh
img="$(grep '^POSTGRES_IMAGE=' .env | cut -d= -f2-)"
docker run --rm -v custom-golf-stats_pgdata:/from:ro -v custom-golf-stats_pgdata_before_restore:/to "$img" \
  cp -a /from/. /to/
docker run --rm -v custom-golf-stats_pgdata_restored:/from:ro -v custom-golf-stats_pgdata:/to "$img" \
  sh -c 'find /to -mindepth 1 -delete && cp -a /from/. /to/'
```

**4. Start the stack on the deployed tag.**

```sh
./deploy.sh "$(grep '^IMAGE_TAG=' .env | cut -d= -f2-)"
```

**5. Take a new base backup.** The restored database is on a new timeline. Later restores start from this backup.

```sh
docker compose exec pg-backup backup-now
```

**6. Clean up.** Once the site checks out, remove the leftover volumes:

```sh
docker volume rm custom-golf-stats_pgdata_restored custom-golf-stats_pgdata_before_restore
```

On a new server, start with a fresh `deploy/.env` that uses the same storage settings. Then:

1. Run step 1.
2. Run `docker compose up --no-start postgres` to create the empty `pgdata` volume.
3. Do the second copy in step 3 (from `pgdata_restored` into `pgdata`).
4. Run steps 4 and 5.

## 4. The drill

`deploy/backup/drill.sh` proves that a restore actually works. It uses the production services from
`deploy/compose.yaml`, plus a MinIO server from `deploy/backup/drill.compose.yaml`:

1. It builds the image and starts MinIO, `postgres` (archiving to MinIO) and `pg-backup`, which takes the first backup.
2. It writes data, then takes a base backup with `backup-now`. Retention is 1, so the first backup has to be deleted.
3. It writes more data, notes a time T, writes a last batch, and waits until that batch's WAL is archived.
4. It deletes the source container and its volume. Only the object storage is left.
5. It runs `restore.sh` twice, each time into a fresh volume:
   - to the latest point: every table's row count and content checksum must match the source at the end;
   - to T: they must match the source at T, without the last batch.

   Both restored servers must be out of recovery.
6. It tears everything down.

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

Once `images.yml` has published the image, set `POSTGRES_IMAGE` to the new SHA tag and deploy.

**Postgres minor versions.** Bump the `FROM` lines the same way.

**Postgres major versions.** A major upgrade, e.g. 18, needs `pg_upgrade` or a dump and restore. It also needs a new
`WALG_S3_PREFIX`, so that backups from the two versions never mix.

## Throwaway stacks

`WALG_S3_PREFIX=none` turns archiving and `pg-backup` off: WAL is discarded. `deploy/smoke.sh` uses it, because the
smoke test checks the app, not backups. Never use it in production.
