# Running Custom Golf Stats in production

This runbook covers:

- setting up the server once;
- the GitHub settings the deploy workflow needs;
- the first deploy, everyday deploys and rollbacks;
- restoring the database;
- importing the golftrack history into production.

It assumes a VPS from any provider running **Ubuntu 24.04 LTS**. Throughout, `example.com` stands for your domain:

- the web site is `example.com`;
- the API is `api.example.com`.

## How it fits together

One Docker Compose stack runs on the server (`deploy/compose.yaml`):

- **caddy**: HTTPS for both names, with Let's Encrypt certificates. It is the only service with published ports (80 and 443).
- **api**: the ASP.NET Core API.
- **web**: the Astro site.
- **postgres**: the database, Postgres 17 with WAL-G. It sits on an internal network and is never exposed. Its only
  other network carries WAL-G's uploads to the backup storage (Backblaze B2 or S3) and publishes nothing.
- **pg-backup**: takes the nightly base backup and applies retention (see the [backup runbook](backups.md)).
- **migrate**: a one-off job that applies EF Core migrations before each deploy.

`.github/workflows/images.yml` builds the images on every push to `master` and pushes them to GHCR. Each image is tagged with the commit SHA, and the Postgres image also with a content tag:

- `ghcr.io/rer145/custom-golf-stats-api:<sha>`
- `ghcr.io/rer145/custom-golf-stats-api:<sha>-migrator`
- `ghcr.io/rer145/custom-golf-stats-web:<sha>`
- `ghcr.io/rer145/custom-golf-stats-postgres:pg-<hash>` (Postgres + WAL-G, from `deploy/postgres`), plus `:<sha>`. Deploys use `pg-<hash>`, a hash of the `deploy/postgres` files (`deploy/postgres-tag.sh`), so a deploy restarts Postgres only when that directory changed. A `pg-<hash>` tag is built once and never rebuilt: later commits with the same files just add their `<sha>` tag to it.

`.github/workflows/deploy.yml` deploys one of those SHAs over SSH. Each run:

1. copies that commit's `deploy/` files to `~/custom-golf-stats/deploy` on the server. `.env` is never copied, and nothing on the server is deleted;
2. logs the server in to GHCR with the job's own short-lived token;
3. runs `deploy/remote-deploy.sh deploy <sha>`, which runs `deploy.sh <sha>`:
   1. pull the images;
   2. start Postgres (recreated only when `deploy/postgres` changed: its image has a content tag, not the SHA);
   3. run the migrations. If they fail, it stops here and the running site is untouched;
   4. `docker compose up -d --wait`, which swaps in the new containers and waits until they are healthy.

   If `deploy.sh` fails, `remote-deploy.sh` deploys the tag that was running before, and the job fails;
4. checks `https://api.example.com/health` and `https://example.com/sign-in`. If either is not answering 200 within two minutes, it rolls back to the previous tag and the job fails;
5. logs the server out of GHCR.

Deploys never overlap. Rerunning a deploy of the tag that is already running changes nothing; it only re-runs the migrations, which have nothing to apply. Such a rerun is never rolled back either, since it was already running.

The server-side steps run detached from the SSH session (`remote-deploy.sh start`, then `wait`). If the connection drops, the deploy or rollback carries on on the server, and the job reconnects and keeps waiting.

`.github/workflows/deploy-test.yml` tests this tooling on every change to `deploy/` or to what goes into the images: `deploy/remote-deploy.test.sh` (the bookkeeping above, with a fake Docker) and `deploy/smoke.sh` (builds the images, deploys the whole stack with `deploy.sh` behind Caddy's internal CA, signs in through it, and checks that a failing migration leaves the running site untouched).

## 1. One-time server setup

### Create the server

- Ubuntu 24.04 LTS, x86-64 or ARM (the images are built for both).
- About 2 vCPU, 2–4 GB RAM and 40 GB of disk is plenty.
- Add your personal SSH public key when you create it, so you log in as `root` (or the provider's admin user) with a key.
- Note its public IPv4 address, and its IPv6 address if it has one.

### DNS

At your DNS provider, create these records:

| Name | Type | Value |
| --- | --- | --- |
| `example.com` | `A` | the server's IPv4 address |
| `api.example.com` | `A` | the server's IPv4 address |
| `example.com` | `AAAA` | the server's IPv6 address (only if it has one) |
| `api.example.com` | `AAAA` | the server's IPv6 address (only if it has one) |

Don't publish an `AAAA` record unless the server really answers on that IPv6 address; Let's Encrypt prefers IPv6 and fails if it doesn't. Wait until both names resolve before the first deploy, because Caddy requests the certificates on its first start:

```bash
dig +short example.com A; dig +short api.example.com A
dig +short example.com AAAA; dig +short api.example.com AAAA
```

### Base system and firewall

As root on the server:

```bash
apt-get update && apt-get -y upgrade
apt-get install -y unattended-upgrades ufw
dpkg-reconfigure -plow unattended-upgrades      # answer Yes: security updates install automatically

ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp                                # HTTP/3
ufw enable
ufw status verbose
```

Docker publishes container ports through its own iptables rules, which bypass ufw. The stack is built with that in mind:

- Caddy is the only service with published ports (80 and 443);
- Postgres has no published port at all.

Never add a `ports:` entry for Postgres.

Turn off SSH password logins. Edit `/etc/ssh/sshd_config.d/10-hardening.conf`:

```
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
```

Then run `systemctl reload ssh`. Before you close the session you're in, check in a second terminal that you can still log in with your key.

### Docker Engine and the Compose plugin

These are Docker's official apt steps for Ubuntu:

```bash
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  > /etc/apt/sources.list.d/docker.list
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
docker version && docker compose version
```

Docker starts on boot. Containers restart by themselves (`restart: unless-stopped`).

### The `deploy` user and its SSH keys

Everything runs as a dedicated `deploy` user. Two keys log in as `deploy`:

- **your own personal key**, for everything you do by hand in this runbook (`ssh deploy@SERVER`);
- **a deploy key** used only by GitHub Actions.

Create the user:

```bash
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
```

> Membership of the `docker` group is equivalent to root on this server. Treat the deploy key as a root key: it lives
> only in the GitHub secret `DEPLOY_SSH_KEY`, and you can revoke it at any time by removing its line from
> `/home/deploy/.ssh/authorized_keys`.

From **your own machine**, add your personal public key (use the `.pub` file you log in to servers with) and check that you can log in as `deploy`:

```bash
ssh root@SERVER 'cat >> /home/deploy/.ssh/authorized_keys && chown deploy:deploy /home/deploy/.ssh/authorized_keys && chmod 600 /home/deploy/.ssh/authorized_keys' < ~/.ssh/id_ed25519.pub
ssh deploy@SERVER 'docker ps'   # must work without sudo
```

Every later command in this runbook that says `ssh deploy@SERVER`, or runs "as `deploy` on the server", uses this login.

Then, still on your machine, create the deploy key pair. It has no passphrase, because a workflow can't type one. Install its public half the same way:

```bash
ssh-keygen -t ed25519 -N '' -C 'custom-golf-stats deploy' -f ./cgs_deploy
ssh deploy@SERVER 'cat >> ~/.ssh/authorized_keys' < ./cgs_deploy.pub
ssh -i ./cgs_deploy -o IdentitiesOnly=yes deploy@SERVER 'docker ps'   # the deploy key works too
```

The private key `./cgs_deploy` goes into the `DEPLOY_SSH_KEY` secret (section 2). Delete your local copy afterwards. If you ever need a new one, generate a new pair and replace its line (the one ending in `custom-golf-stats deploy`) in `/home/deploy/.ssh/authorized_keys`.

### The pinned host key

The workflow only connects to a server whose host key it already knows. Host key checking is never turned off. Get the key from your machine:

```bash
ssh-keyscan -t ed25519 SERVER > ./cgs_known_hosts            # with a non-standard port: ssh-keyscan -p PORT -t ed25519 SERVER
ssh-keygen -lf ./cgs_known_hosts                             # prints the fingerprint
```

Compare that fingerprint with the server's own, read over the provider's console or your existing root session:

```bash
ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
```

They must match. Use exactly the same host name or IP in `ssh-keyscan` as in the `DEPLOY_HOST` secret, because the known_hosts line is keyed by it. If you rebuild the server, its host key changes; set `DEPLOY_KNOWN_HOSTS` again.

### The `.env` file

The stack's settings and secrets live in one file, `~/custom-golf-stats/deploy/.env`, owned by `deploy` and readable by nobody else. The workflow never copies, prints or overwrites it.

From your checkout, copy the template up and lock it down:

```bash
ssh deploy@SERVER 'mkdir -p ~/custom-golf-stats/deploy'
scp deploy/.env.example deploy@SERVER:custom-golf-stats/deploy/.env
ssh deploy@SERVER 'chmod 600 ~/custom-golf-stats/deploy/.env'
```

Then edit it on the server (`ssh deploy@SERVER`, then `nano ~/custom-golf-stats/deploy/.env`). Generate secrets on the server, never in a chat or a shared document.

| Variable | What to put there |
| --- | --- |
| `WEB_DOMAIN` | The web site's host name, e.g. `example.com`. Caddy gets its certificate. |
| `API_DOMAIN` | The API's host name, e.g. `api.example.com`. |
| `ACME_EMAIL` | Your email address. Let's Encrypt sends certificate expiry warnings there. |
| `HTTP_PORT`, `HTTPS_PORT` | Leave `80` and `443`. |
| `IMAGE_TAG` | Leave the placeholder. `deploy.sh` (run by `remote-deploy.sh`) sets the tag for each deploy, so this value is never used for deploys. For manual `docker compose` commands, use `remote-deploy.sh compose ...`, which sets the deployed tag for you. |
| `API_IMAGE`, `WEB_IMAGE`, `POSTGRES_IMAGE` | Leave as they are (`ghcr.io/rer145/custom-golf-stats-api` / `-web` / `-postgres`). They are repositories without a tag: every deploy uses the deployed `IMAGE_TAG` for the API and web images, and the content tag of `deploy/postgres` for the Postgres image. |
| `POSTGRES_TAG` | Leave unset. It pins the Postgres image to another tag instead of the content tag. |
| `CADDY_IMAGE` | Leave as it is. It is a pinned version, bumped deliberately in the repository. |
| `POSTGRES_DB`, `POSTGRES_USER` | Leave `custom_golf_stats`. |
| `POSTGRES_PASSWORD` | Run `openssl rand -hex 32` and paste the result. It must not contain `;` or quote characters, because it is placed inside the API's connection string. Hex output never does. It is only used inside the Compose network. Set it before the first start: Postgres only reads it when it creates the database. To change it later, run `ALTER USER` in the database as well. |
| `GOLF_COURSE_API_KEY` | Your golfcourseapi.com key, used for course search and import. Leave it empty to turn those calls off. |
| `OPEN_METEO_FORECAST_URL`, `OPEN_METEO_ARCHIVE_URL`, `OPEN_METEO_API_KEY` | Keep the free endpoints and an empty key, until a commercial Open-Meteo plan (needed before a paid launch) gives you its own URLs and key. |
| `AUTH_APPLE_CLIENT_IDS` | The audiences accepted for Sign in with Apple, comma-separated. That is the app's bundle id `xyz.rsquaredsolutions.customgolfstats`, plus the web Services ID if you add Apple sign-in to the web site. |
| `AUTH_GOOGLE_CLIENT_IDS` | The OAuth client ids accepted for Google sign-in, comma-separated (`…apps.googleusercontent.com`). Leave it empty until Google sign-in exists. |
| `PROXY_SUBNET` | Leave `172.30.0.0/24`. Change it only if it collides with a network that already exists on the server. The API trusts forwarded headers only from this subnet. |
| `SESSION_SECRET` | Run `openssl rand -base64 48` and paste the result. It must be at least 32 characters. It encrypts the web session cookie, so changing it signs everyone out of the web site. |
| `WALG_S3_PREFIX` | Required. Where the backups go, `s3://<bucket>/<path>`, e.g. `s3://cgs-db-backups/custom-golf-stats`. Use a prefix for this database alone. Create the bucket first: [backup runbook](backups.md), section 1. |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | Required. The storage key limited to that bucket: a B2 application key, or an S3 IAM user's access key. |
| `AWS_ENDPOINT`, `AWS_REGION` | Required. The storage's S3 endpoint and region, e.g. `https://s3.us-west-004.backblazeb2.com` and `us-west-004` for B2, or `https://s3.eu-west-1.amazonaws.com` and `eu-west-1` for AWS. |
| `AWS_S3_FORCE_PATH_STYLE` | Leave `false` for B2 and AWS. |
| `BACKUP_TIME` | Optional. The nightly base backup time, `HH:MM` in UTC (default `03:00`). |
| `WALG_RETAIN_FULL` | Optional. How many nightly full backups to keep (default `7`, about a week of point-in-time restore). |

Check that the file is private:

```bash
ls -l ~/custom-golf-stats/deploy/.env    # -rw------- deploy deploy
```

## 2. GitHub settings

All of these are **repository** secrets and variables, under Settings > Secrets and variables > Actions. Set them with the `gh` CLI from your checkout. Secrets are read from stdin or a file, so they never end up in your shell history.

### Secrets

| Secret | Value |
| --- | --- |
| `DEPLOY_HOST` | The server's host name or IP. It must be the same one you used with `ssh-keyscan`. |
| `DEPLOY_USER` | `deploy` |
| `DEPLOY_SSH_KEY` | The whole private key file `cgs_deploy`. |
| `DEPLOY_KNOWN_HOSTS` | The pinned host key line(s) from `cgs_known_hosts`. |
| `DEPLOY_PORT` | Optional. The SSH port, if it is not 22. |

```bash
gh secret set DEPLOY_HOST                       # prompts for the value
gh secret set DEPLOY_USER --body deploy
gh secret set DEPLOY_SSH_KEY < ./cgs_deploy
gh secret set DEPLOY_KNOWN_HOSTS < ./cgs_known_hosts
# gh secret set DEPLOY_PORT --body 2222         # only for a non-standard port
rm ./cgs_deploy ./cgs_deploy.pub ./cgs_known_hosts
```

### Variables

| Variable | Value | Used by |
| --- | --- | --- |
| `PUBLIC_API_URL` | `https://api.example.com` | the deploy workflow's health check (`/health`) |
| `PUBLIC_WEB_URL` | `https://example.com` | the deploy workflow's health check (`/sign-in`) |
| `DEPLOY_ON_MERGE` | `true` to deploy automatically after every successful image build on `master`. Leave it unset until the first manual deploy has worked. | `deploy.yml` |
| `WEB_ALLOWED_HOSTS` | `example.com`, the same as `WEB_DOMAIN`. It is baked into the web image at build time. If it is wrong, every web sign-in is refused with 403. | `images.yml` |
| `API_BASE_URL` | `https://api.example.com/` (with the trailing slash). It is the address the iOS and Android release builds call. | `mobile.yml` |

```bash
gh variable set PUBLIC_API_URL --body https://api.example.com
gh variable set PUBLIC_WEB_URL --body https://example.com
gh variable set WEB_ALLOWED_HOSTS --body example.com
gh variable set API_BASE_URL --body https://api.example.com/
# later, once a manual deploy has worked:
gh variable set DEPLOY_ON_MERGE --body true
```

`WEB_ALLOWED_HOSTS` and `API_BASE_URL` are build-time settings: changing one does nothing until the images (or the app) are rebuilt.

- **Web:** after setting or changing `WEB_ALLOWED_HOSTS`, run the Images workflow on `master` (`gh workflow run images.yml --ref master`), then deploy the new SHA.
- **App:** a change to `API_BASE_URL` reaches users only with the next app build (`gh workflow run mobile.yml --ref master`) and its TestFlight upload. See [testflight.md](testflight.md).

If any of the deploy secrets, `PUBLIC_API_URL` or `PUBLIC_WEB_URL` is missing, the deploy workflow does nothing. It lists the missing names and still ends green.

### Registry access (GHCR)

The server pulls the images with the **deploy job's own `GITHUB_TOKEN`**:

- its scope is what `deploy.yml` grants: `contents: read`, `actions: read` and `packages: read`. It can read this repository and its packages, and write nothing;
- it is sent to the server over SSH on stdin, so it never appears on a command line;
- it expires when the job ends, and the job logs out again at the end.

Nothing long-lived is stored on the server, and the packages can stay private even though the repository is public. Its workflow logs are public too, so the workflows print no secrets (GitHub masks them as well). This works because `images.yml` publishes the packages from this repository, which gives this repository's workflows read access.

The other options were considered and rejected:

- **A fine-grained personal access token:** not usable. GHCR does not accept fine-grained tokens.
- **A classic PAT with `read:packages`:** it works, but it is a long-lived credential sitting on the server, and it can read every package your account can see. Avoid it.
- **Making the packages public** (package settings > Change visibility): this removes the need for any login. It is safe, because the images contain no secrets. The repository is public, so the source is public already; this would add the compiled API and web site. Do it only if you are happy for those to be public too. The workflow's login is harmless either way.

The trade-off: outside a workflow run, the server holds no registry credentials. It can't pull new images by hand unless the packages are public. It doesn't need to:

- running containers and server restarts use images already on disk;
- rollbacks, and `remote-deploy.sh deploy --no-pull`, use the images on disk. They contact the registry only for an image that is missing;
- every other deploy goes through the workflow.

There are three packages: `custom-golf-stats-api` (also holds the `-migrator` tags), `custom-golf-stats-web` and `custom-golf-stats-postgres`. The postgres one appears with the first Images run that includes it. If a deploy fails with `denied` while pulling, open each package's settings on GitHub (your profile > Packages). Check that it is connected to this repository ("Connect repository" if not). Then, under "Manage Actions access", give this repository **Read** access. A deploy that pulls the postgres image for the first time needs this as much as the other two.

## 3. First deploy

1. Check that DNS resolves (section 1), that `.env` is filled in (the backup storage included: create the bucket and
   key first, [backup runbook](backups.md) section 1), and that the secrets and variables are set.
2. Make sure a successful Images run on `master` was built *after* `WEB_ALLOWED_HOSTS` was set. If not, start one with `gh workflow run images.yml --ref master` and wait for it to finish.
3. Run the deploy, either from Actions > Deploy > Run workflow (leave the tag empty for the latest successful Images run) or with the CLI:

   ```bash
   gh workflow run deploy.yml --ref master                    # latest successful image build on master
   gh workflow run deploy.yml --ref master -f tag=<full sha>  # a specific commit
   gh run watch
   ```

4. The first start takes a little longer, because Caddy requests the certificates. If the health check fails, look at Caddy's log (section 4). The usual causes are DNS not resolving yet, or a wrong `AAAA` record.
5. Open `https://example.com` and register your account.
6. Check the backups. On the server, `cd ~/custom-golf-stats/deploy`; then `./remote-deploy.sh compose logs pg-backup` shows `base backup finished` within a
   few minutes, and `./remote-deploy.sh compose exec pg-backup wal-g backup-list` lists it ([backup runbook](backups.md)
   section 2).
7. When you are happy, set `DEPLOY_ON_MERGE=true`. From then on, every merge to `master` deploys itself once its images are built.

## 4. Everyday operations

All of these run as `deploy` on the server (`ssh deploy@SERVER`):

```bash
cd ~/custom-golf-stats/deploy
./remote-deploy.sh status                 # current and previous tag, and the deploy history
./remote-deploy.sh compose ps             # docker compose with the deployed IMAGE_TAG (and the Postgres content tag) set
./remote-deploy.sh compose logs -f --tail=100 api
./remote-deploy.sh compose logs caddy     # certificate problems show up here
./remote-deploy.sh compose restart web
```

The deploy state lives in `~/custom-golf-stats/state/`: `current-tag`, `previous-tag` and `history.log`.

To deploy a specific commit, run the Deploy workflow with its full SHA. It must be a commit on `master` whose Images run succeeded.

Disk: old images stay on disk so that rollbacks don't need to pull them. Now and then, clear out the old ones:

```bash
docker image prune -a --filter "until=720h"   # removes images that no container uses and that were built over 30 days ago
```

This can also remove the previous tag's images, if they are that old. A rollback through the workflow pulls them again. A rollback run on the server can't pull them (see "Registry access").

Long commands by hand: a deploy run from your SSH session stops if the connection drops. Run it detached instead, and follow it (you can run `wait` again after reconnecting):

```bash
./remote-deploy.sh wait "$(./remote-deploy.sh start deploy --no-pull <sha>)"
```

The workflow copies the new `deploy/` files **before** it takes the server's deploy lock. Don't run `remote-deploy.sh` by hand while a Deploy workflow is running: it could pick up the new `compose.yaml` halfway through.

## 5. Rollback

**Automatic.** A deploy goes back on its own when:

- `deploy.sh` fails (a migration fails, an image is missing, or a container never becomes healthy): `remote-deploy.sh` deploys the tag that was running before;
- the public health check fails after the deploy: the workflow runs `remote-deploy.sh rollback <sha>`, which deploys the previous tag again. This doesn't happen when the deployed tag was already running before the deploy (a rerun): rolling back would replace a version that was already live.

Rollbacks and these restores use the images already on disk, and pull only an image that is missing.

The **first deploy** has nothing to roll back to. If it fails, the job fails and the stack stays as the failed deploy left it. Fix the problem and deploy again.

Either way, the job fails, so you see it.

**Manual. The usual way:** run the Deploy workflow with the older commit's SHA. That puts back both its images and its `deploy/` files, and records it like any other deploy. `./remote-deploy.sh status` on the server lists the recent tags, and so does `gh run list --workflow images.yml --branch master`.

**Manual, on the server, if GitHub is unavailable:**

```bash
cd ~/custom-golf-stats/deploy
./remote-deploy.sh rollback                      # back to the previous tag, from the images on disk
./remote-deploy.sh deploy --no-pull <older sha>  # or any tag whose images are on disk (docker images | grep custom-golf-stats)
```

What a rollback does **not** undo:

- **Database migrations.** Migrations only move forward. A rollback runs the old images against the newer schema. That works as long as migrations are additive (new tables or columns, nothing dropped or renamed in the same release), and every migration should be written that way.

  If a release's migration destroyed or rewrote data, restore the database (section 6) to a time before the deploy, then deploy the older tag.
- **The `deploy/` files on the server**, when the rollback is run on the server or done automatically. The workflow's own rollbacks keep the newer `compose.yaml` and `Caddyfile`, and Postgres stays on the image of the newer `deploy/postgres` (it isn't restarted). Run the Deploy workflow with the older SHA to restore those files, and that commit's Postgres image, too.

## 6. Backups and restore

Backups (WAL-G archiving, nightly base backups and retention) and the scripted restore, with its drill, are documented in the [backup and restore runbook](backups.md), next to the scripts in `deploy/backup/`.

To restore production after a bad release:

1. restore to a point in time before the deploy and put it into service, following "Restoring production" in that runbook;
2. deploy the last good tag (section 5).

Restores use the Postgres image the stack runs. They run alongside the live site until the swap.

## 7. Importing the golftrack history into production

The phase 3 import tool (`src/CustomGolfStats.Migration`, runbook [docs/migration/README.md](../migration/README.md)) runs on **your machine**. It reads the golftrack backup restored into a local SQL Server container, exactly as in that runbook. It writes to production Postgres through a temporary, SSH-only tunnel. Postgres itself stays unexposed: the tunnel listens only on the server's loopback interface, and only for as long as the import runs.

Do it **before any device signs in and syncs the account**. Re-running the import hard-deletes and recreates the imported rounds, and synced devices would not hear about any that disappear.

1. **Take a backup first.** Follow the [backup and restore runbook](backups.md) to run an on-demand base backup.
2. **Create your account** on the production web site (or in the app), with the email you'll pass as `--account-email`. The deploy has already applied every migration; the tool checks that.
3. **Restore the golftrack backup locally** (sections 1–2 of the migration runbook).
4. **Open the tunnel.** On the server (`ssh deploy@SERVER`), start a small proxy. It sits on the default bridge, published on the server's loopback only, and joined to the stack's internal network:

   ```bash
   docker run -d --rm --name cgs-pg-tunnel -p 127.0.0.1:15432:5432 alpine:3.22 \
     sh -c 'apk add --no-cache socat >/dev/null && exec socat TCP-LISTEN:5432,fork,reuseaddr TCP:postgres:5432'
   docker network connect custom-golf-stats_backend cgs-pg-tunnel
   ```

   Then, on your machine, forward a local port to it and leave this running:

   ```bash
   ssh -N -L 15432:127.0.0.1:15432 deploy@SERVER
   ```

5. **Point the tool at production.** In another terminal on your machine, set the target without printing the password:

   ```bash
   export GOLFTRACK_SOURCE="Server=127.0.0.1,14333;Database=golftrack;User Id=sa;Password=$SA;TrustServerCertificate=True"
   PGPW="$(ssh deploy@SERVER 'sed -n "s/^POSTGRES_PASSWORD=//p" custom-golf-stats/deploy/.env')"
   export CGS_TARGET="Host=127.0.0.1;Port=15432;Database=custom_golf_stats;Username=custom_golf_stats;Password=$PGPW"
   unset PGPW
   ```

6. **Dry run, then import, then the parity check**, using the commands in sections 4 and 5 of the migration runbook:

   ```bash
   dotnet run --project src/CustomGolfStats.Migration -- import --golfer-id 1 --account-email you@example.com --dry-run --report import-dryrun.md
   dotnet run --project src/CustomGolfStats.Migration -- import --golfer-id 1 --account-email you@example.com --report import.md
   dotnet run --project src/CustomGolfStats.Migration -- parity --golfer-id 1 --account-email you@example.com --report parity.md
   ```

   Read the reports before you go on. They stay on your machine; don't commit them.

7. **Close everything:**
   - stop the `ssh -N` tunnel (Ctrl-C);
   - on the server, run `docker rm -f cgs-pg-tunnel`;
   - on your machine, run `unset CGS_TARGET GOLFTRACK_SOURCE`, then `docker rm -f cgs-golftrack`.
8. **Fix up the data**, using the follow-ups noted from the local run (`docs/superpowers/plans/2026-11-11-v1-roadmap.md`, phase 3 carry-over):
   - set ratings and slopes for the tees imported as 0/113;
   - merge tees that were really versions of one tee, with the admin tools.
9. **Only then** sign in on a device.

## 8. Troubleshooting

| Symptom | What to check |
| --- | --- |
| Deploy job says "Deploy skipped … Missing: …" | Set the listed secrets or variables (section 2). |
| `Host key verification failed` | The server's host key changed (a rebuilt server?), or `DEPLOY_HOST` doesn't match the name in `DEPLOY_KNOWN_HOSTS`. Redo "The pinned host key", after checking the fingerprint. |
| `Permission denied (publickey)` | Check that the key's public half is in `/home/deploy/.ssh/authorized_keys`, with mode 600 and owner `deploy`. Check that `DEPLOY_USER` is `deploy`. |
| `denied` while pulling | GHCR access for this repository (end of section 2). |
| `Migrations failed; the running services were left untouched` | Read the migration error in the job log. The old version is still serving. Fix the migration and merge again. |
| The health check fails but the containers are healthy | Caddy can't get a certificate (DNS, or ports 80/443 blocked at the provider's firewall). Run `./remote-deploy.sh compose logs caddy`. |
| Web sign-in answers 403 | `WEB_ALLOWED_HOSTS` doesn't match the web host name. Fix the variable, rebuild the images, then deploy. |
| `another deploy is running on this server` | A manual `remote-deploy.sh` is running at the same time as the workflow. Wait for it to finish. |
| `remote-deploy.sh` exits 3 | The deploy failed and putting the old tag back failed too. Run `./remote-deploy.sh compose ps` and look at the logs, then deploy a known-good tag. |
