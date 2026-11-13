# Migrating golftrack history

`src/CustomGolfStats.Migration` copies one golftrack golfer's history (courses, tees, holes, rounds, hole details, handicap history) into a Custom Golf Stats account. A second command, `parity`, then compares the new stats with golftrack's own stored procedures, year by year.

This process never needs credentials for the real golftrack server. You work from a backup restored into a throwaway local container.

## 1. Export a backup

- **SQL Server:** take a full `.bak` backup of the golftrack database.
- **Azure SQL:** export a `.bacpac` (in the portal or with `SqlPackage /Action:Export`).

Put the file anywhere on your machine. `*.bak` and `*.bacpac` are git-ignored; never commit a backup.

## 2. Restore it into a local SQL Server

```bash
SA=$(openssl rand -hex 16)Aa1!
docker run -d --name cgs-golftrack --platform linux/amd64 -p 127.0.0.1:14333:1433 \
  -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$SA" mcr.microsoft.com/mssql/server:2022-latest
docker exec cgs-golftrack mkdir -p /var/opt/mssql/backup
docker cp path/to/golftrack.bak cgs-golftrack:/var/opt/mssql/backup/golftrack.bak
docker exec -u 0 cgs-golftrack chown mssql /var/opt/mssql/backup/golftrack.bak
docker exec cgs-golftrack /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA" \
  -Q "RESTORE FILELISTONLY FROM DISK='/var/opt/mssql/backup/golftrack.bak'"
# use the two logical names it prints:
docker exec cgs-golftrack /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA" \
  -Q "RESTORE DATABASE golftrack FROM DISK='/var/opt/mssql/backup/golftrack.bak' WITH MOVE '<data name>' TO '/var/opt/mssql/data/golftrack.mdf', MOVE '<log name>' TO '/var/opt/mssql/data/golftrack.ldf'"
```

The `linux/amd64` image runs on Apple Silicon under Docker's emulation; give it a minute to start. For a `.bacpac`, import it with `SqlPackage /Action:Import` into the same container instead.

`SA` is a throwaway password for this local container only.

## 3. Prepare the target

1. Make sure the target Postgres schema is fully migrated. The API applies its migrations on start **only in Development**; otherwise apply them with `dotnet ef database update --project src/CustomGolfStats.Api` (or a migrations bundle). The tool never migrates; it exits with code 2 if migrations are pending.
2. Create the golfer's account through the API (`POST /auth/register`, or sign in with Apple/Google). The tool looks the account up by email and never creates accounts.

## 4. Import

Connection strings come from environment variables (or `--source` / `--target`). They are never printed.

```bash
export GOLFTRACK_SOURCE="Server=127.0.0.1,14333;Database=golftrack;User Id=sa;Password=$SA;TrustServerCertificate=True"
export CGS_TARGET="Host=...;Port=5432;Database=...;Username=...;Password=..."

dotnet run --project src/CustomGolfStats.Migration -- import --golfer-id 1 --account-email you@example.com --dry-run --report import-dryrun.md
dotnet run --project src/CustomGolfStats.Migration -- import --golfer-id 1 --account-email you@example.com --report import.md
```

- `--dry-run` does everything except commit.
- `--replace-other-golfer` is needed only when the account already holds rounds or handicap entries imported from a *different* golftrack golfer; see "Re-running".
- The report lists counts per year, skipped rounds, duplicate rounds, holes with zero putts, tees with missing ratings, durations it couldn't read, rounds moved to the tee of their holes, and anything outside the API's ranges.
- Exit codes:
  - `0`: success.
  - `2`: something you can fix. That covers bad arguments, an unusable report path, an unknown account, a golfer with no active rounds, an import over another golfer's data without `--replace-other-golfer`, a target with pending migrations, or rows already imported into another account. Nothing was changed.
  - `1`: connection failures and anything unexpected. The message says whether the import was committed.

### What gets imported

- Only rounds and hole details marked active in golftrack. Every course, tee and hole those rounds use is imported, even if golftrack marks it inactive (golftrack's stats ignore that flag too).
- Courses are stored as `Verified`, with one tee version per golftrack tee, effective from 1900-01-01. Missing ratings become 0 with slope 113 for an admin to correct.
- golftrack's single sand-shot count becomes greenside sand shots.
- Weather, wind and temperature categories are kept as text in the round's legacy weather field.

### Re-running

Running `import` again is safe. It clears what the previous run imported into that account and imports again in one transaction. Rows come out the same, with the same ids, but their timestamps change.

Because of that, **import before anyone edits the imported data in the app, and before any device syncs the account.** A re-run hard-deletes the imported rounds and recreates them; it does not soft-delete them. As a result:

- Edits to imported rounds, and to golftrack courses that nothing else uses, are lost.
- A device that already synced the imported rounds gets no tombstones for any round that a re-run does not recreate.

A `--golfer-id` with no active rounds is refused (exit 2), so a typo can't wipe the previous import. Importing a *different* golftrack golfer into an account that already holds another golfer's imported rounds or handicap entries is also refused (exit 2), unless you pass `--replace-other-golfer`. With the flag, those rows are cleared and replaced, and the tool prints a warning with the counts ("would clear" on a dry run).

## 5. Parity check

```bash
dotnet run --project src/CustomGolfStats.Migration -- parity --golfer-id 1 --account-email you@example.com --report parity.md
```

The tool runs golftrack's `Stats_*`, `Round_SeasonSummary`, `Stats_CareerStats` and `Stats_CourseStats` procedures, which are read-only. It compares every column that has a counterpart with the new stats engine on the imported rounds. Each row gets one of these verdicts:

- **match**: equal. Counts, to-par figures and dates must be exact. Averages and percentages may differ by up to ±0.006.
- **intended**: a documented change of definition (see the report's rule table). Both sides are recomputed independently to prove that the rule is the only reason for the difference.
- **unexplained**: anything else. Investigate these.
- **not compared** / **missing**: golftrack columns with no counterpart, missing columns and failed procedures.

Some procedures take only a year and include every golfer's rounds. The report notes this where it applies.

## 6. Clean up

```bash
docker rm -f cgs-golftrack
```
