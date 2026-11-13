# Phase 6: Deployment and TestFlight Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Get the API and web site running on Ron's VPS behind HTTPS with backed-up Postgres, and get a signed iOS build into TestFlight from the cloud. That's the first time real testers can use the app. The second goal is closing the store blocker: in-app account deletion on the API, the app and the web site.

**Architecture:** One Docker Compose stack on the VPS:
- `caddy`: automatic HTTPS for `api.<domain>` and `<domain>`.
- `api`: the ASP.NET Core image.
- `web`: the Astro Node image.
- `postgres`: Postgres 17 with WAL-G archiving to S3-compatible storage.
- `migrate`: a one-shot service that runs the EF Core migrations bundle before `api` starts.

Images are built by GitHub Actions and pushed to GitHub Container Registry (GHCR). A deploy workflow connects to the VPS over SSH with a deploy key, pulls the images and runs `docker compose up -d`. iOS signing uses an App Store Connect API key stored as GitHub secrets; the mobile workflow from PR #4 gains a signed IPA and a TestFlight upload. Everything secret lives in GitHub environment secrets or the VPS `.env`, never in the repo.

**Tech stack:**
- Docker (multi-stage builds)
- Docker Compose v2
- Caddy 2
- Postgres 17
- WAL-G
- GitHub Actions, GHCR
- App Store Connect API (TestFlight upload)

**Spec:** `docs/superpowers/specs/2026-11-10-custom-golf-stats-v1-design.md` §9. Roadmap carry-overs are in `docs/superpowers/plans/2026-11-11-v1-roadmap.md`. They cover:
- migrations only run automatically in Development;
- a fixed data-protection application name;
- `ALLOWED_HOSTS` and `ApiBaseUrl` at build time;
- the store blockers;
- the iOS Release trim decision, verified on TestFlight.

## Global Constraints

- **No secrets in the repo, logs or images.**
  - `deploy/.env.example` documents every variable with placeholders.
  - Real values live in the VPS `deploy/.env` (mode 600) and in GitHub environment secrets.
  - Images never contain secrets or a `.env`.
- **Images.**
  - The API and web images run as a non-root user and include a healthcheck.
  - Images are tagged with the git SHA and `latest`.
  - The API image runs with `ASPNETCORE_ENVIRONMENT=Production`.
- **Data protection.** Keys stay in Postgres, as today. Set a fixed application name (`CustomGolfStats`) so restarts and image changes don't sign everyone out.
- **Migrations.**
  - Applied by the `migrate` service from an EF Core migrations bundle built in the API image stage.
  - `api` starts only after `migrate` completes successfully.
  - Production never calls `Database.Migrate()` from the app.
- **Network exposure.**
  - Postgres is never exposed outside the Compose network.
  - Caddy is the only service with published ports (80 and 443).
  - The API listens on HTTP inside the network; Caddy terminates TLS.
  - Forwarded headers are trusted only from the Compose network.
- **Backups.**
  - WAL-G continuous WAL archiving plus a nightly base backup and a retention policy.
  - The restore procedure is scripted and proven by an automated drill that restores into a scratch Postgres and runs a row-count check.
  - The drill runs locally against MinIO, and in CI.
- **Account deletion** (`DELETE /me`). It removes the account and everything it owns:
  - rounds, holes and shots;
  - handicap entries;
  - its flags as reporter;
  - its sessions and tokens (no tokens remain valid).

  Shared courses stay, with `CreatedByAccountId` cleared. It requires the current password, or a fresh external sign-in token, as confirmation. The app and the web site each get a clearly warned delete flow, and the app clears its local data afterwards.
- **iOS.**
  - Bundle id `com.customgolfstats.app`.
  - The build number is the GitHub run number, so every upload is unique.
  - Signing is automatic via the App Store Connect API key.
  - The TestFlight upload job runs only when the secrets exist, so forks and PRs skip it.
  - Android Play upload is out of scope for this phase; the AAB keeps building.

## Review Focus

1. **Secrets:** nothing secret is committed, baked into an image, echoed in a workflow log or passed on a command line visible in `ps`.
2. **Restore actually works:** the drill restores from WAL-G storage into an empty Postgres, and the restored data is checked.
3. **Deletion is complete and safe:** no orphaned owned rows, and other accounts' data is untouched. Shared courses survive. Re-authentication is required. The app stops syncing and clears local data afterwards.
4. **The deploy is repeatable and reversible:** rerunning the deploy is a no-op. A failed migration doesn't take the old API down. A rollback to the previous image tag is documented.

---

## Tasks

### Task 1: Container images and the Compose stack
- `src/CustomGolfStats.Api/Dockerfile`: multi-stage build with SDK 10 → aspnet 10 runtime. It also builds an EF migrations bundle into a separate `migrator` target.
- `web/Dockerfile`: Node 24, `npm ci`, `ALLOWED_HOSTS` as a build arg, runtime image with only `dist` and production dependencies.
- `deploy/compose.yaml`, `deploy/Caddyfile` (two sites, security headers, gzip/zstd), `deploy/.env.example`.
- API production config: set the data-protection application name; trust forwarded headers from the proxy network; disable the HTTPS redirect behind Caddy if one exists; run migrations only through the bundle.
- A local smoke test, `deploy/smoke.sh`:
  - brings the stack up with Caddy's internal CA on `*.localhost`;
  - waits for health;
  - registers a user, logs in and calls `/me` through Caddy;
  - signs in on the web site;
  - tears down.
- A GHCR publish workflow (`.github/workflows/images.yml`) on master and manual dispatch, multi-arch amd64/arm64.

### Task 2: Backups and restore
- Postgres image with WAL-G (`deploy/postgres/Dockerfile`):
  - `archive_mode` and `archive_command`;
  - a nightly base backup via a small cron sidecar or a systemd timer;
  - retention of 7 full backups;
  - configured from `.env` (`WALG_S3_PREFIX`, keys, endpoint, so B2's S3 API works).
- `deploy/backup/restore.sh`, plus an automated drill, `deploy/backup/drill.sh`:
  - start MinIO;
  - start Postgres archiving to MinIO;
  - write data;
  - take a base backup and generate more WAL;
  - restore into a fresh volume (point-in-time to latest);
  - compare row counts.

  The drill runs in CI on ubuntu.

### Task 3: Deploy workflow and server runbook
- `.github/workflows/deploy.yml`, run by manual dispatch and on images published from master, against the GitHub environment `production`. Steps:
  - SSH with a deploy key;
  - `docker compose pull`, then `up -d --wait`;
  - verify health through the public URL;
  - on failure, roll back to the previous image tags.
- `docs/deploy/README.md`:
  - one-time server setup (Docker, deploy user, firewall 22/80/443, DNS records, `.env` creation);
  - the GitHub secrets list;
  - first deploy, rollback and restore;
  - how to run the golftrack import (phase 3) against production.

### Task 4: Account deletion
- API: `DELETE /me` with `{ password? , externalIdToken? }` re-authentication. Cascade or explicit deletes in one transaction, then sign-out of all sessions (security stamp). Tests: complete deletion, other accounts untouched, courses survive, wrong password rejected, tokens invalid afterwards.
- OpenAPI document and generated client regenerated.
- App:
  - Profile → "Delete account": a confirm dialog explaining permanence and unsynced rounds; asks for the password.
  - Then clear local data and return to sign-in.
  - App.Core tests.
- Web: Profile → "Delete account" page with a password, then sign out. e2e test.

### Task 5: TestFlight from the cloud
- Extend `.github/workflows/mobile.yml` with an `ios-testflight` job (master and manual dispatch, `production` environment, skipped without secrets). Steps:
  - import the App Store Connect API key;
  - automatic signing (`-p:CodesignKey`/provisioning via the API key, or `xcodebuild -allowProvisioningUpdates` with the key);
  - `ApplicationVersion` = run number;
  - `ApiBaseUrl` from `vars.API_BASE_URL`;
  - build the signed IPA;
  - upload with `xcrun altool --upload-app` (or `apple-actions/upload-testflight-build`) using the API key.
- Info.plist export compliance (`ITSAppUsesNonExemptEncryption` false) and privacy manifest completeness (name, GHIN, user content, per the review).
- `docs/deploy/testflight.md`: Apple Developer enrollment, the bundle id and App Store Connect app record, API key creation, which secrets go where, and adding testers.

### Task 6: Go live (controller with Ron)
- With Ron's accounts in place:
  - set the GitHub secrets and variables;
  - configure DNS;
  - first deploy;
  - restore drill against real B2/S3;
  - first TestFlight build;
  - production golftrack import.
- Final whole-branch review, then the PR.
