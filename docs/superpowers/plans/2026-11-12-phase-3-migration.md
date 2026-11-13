# Phase 3: golftrack Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A console tool, `CustomGolfStats.Migration`, that copies Ron's golftrack history (SQL Server) into the new Postgres database for his account, plus a parity report that compares the new stats engine with golftrack's own stored procedures, year by year.

**Architecture:** .NET 10 console app with two commands, `import` and `parity`. It reads golftrack with `Microsoft.Data.SqlClient` (plain ADO.NET, no EF on the SQL Server side) and writes through the API project's `AppDbContext`, so it uses the same schema, configurations and migrations. The mapping from golftrack rows to new entities is a pure, unit-tested class. Ron's real data is never needed by the code or tests. Development and tests use a throwaway SQL Server 2022 container (linux/amd64 image; verified on this Mac under Docker emulation) loaded with golftrack's schema from `docs/reference/golftrack/01-tables.sql` and synthetic rows. The real run restores Ron's backup into a local throwaway container.

**Tech Stack:** .NET 10 console, `System.CommandLine`, `Microsoft.Data.SqlClient`, EF Core (via the API project), xUnit v3, Testcontainers (`Testcontainers.MsSql`, `Testcontainers.PostgreSql`).

**Spec:** `docs/superpowers/specs/2026-11-10-custom-golf-stats-v1-design.md` §3.7 and §10. Roadmap: `docs/superpowers/plans/2026-11-11-v1-roadmap.md` (phase 1–2 carry-overs).

## Global Constraints

- Phase 1/2 rules apply (net10.0, nullable, warnings as errors, central package management, no FluentAssertions/AutoMapper/MediatR).
- **No credentials in code, config files, tests or logs.** Connection strings come only from the environment variables `GOLFTRACK_SOURCE` (SQL Server) and `CGS_TARGET` (Postgres), or `--source`/`--target` options. Never print a connection string; mask it if a message must mention it.
- `golftrack-xyz` is read-only and never edited. The tool only ever runs `SELECT` statements and the read-only `Stats_*`/`Round_SeasonSummary`/`Chart_*` procedures against the source.
- The target account must already exist (created through the API). The tool looks it up by `--account-email`. It never creates or changes accounts beyond writing handicap entries.
- **Re-runnable.** Every imported row gets a deterministic id: a UUID v5 from a fixed namespace plus `"golftrack:<table>:<id>"`. Courses are stored with `Source = Golftrack`, `ProviderCourseId = <golftrack Course.Id>`. A re-run deletes the account's previously imported rounds (by their deterministic ids), handicap entries it created and any golftrack courses no other account's rounds use, then imports again, all in one transaction. `--dry-run` does everything except commit.
- Write rows directly with `AppDbContext`, **not** through `RoundUpsertService` (it counts trials and calls weather).
- Mapping rules (spec §3.7 and roadmap carry-overs):
  - Courses, tees and holes → Status `Verified`, Source `Golftrack`, one `TeeVersion` per golftrack tee with `EffectiveFrom = 1900-01-01`. Ratings/slopes may be null in golftrack: store front/back as null; for the 18-hole rating/slope, missing values become 0 / 113 (the same rule as imports) and are listed in the report.
  - Stroke index: golftrack `Hole.Handicap`, normalised to 1..n with the same rule as GolfCourseAPI import (`CourseImport.NormalizeStrokeIndexes`).
  - Rounds → `EntryMode = Quick`, `PlayedOn` = date of `PlayedOn`, `RoundType` by name (`Practice|Casual|Tournament|League`, unknown → Casual, listed in the report), `Notes`, `DistanceMiles` from `Distance`, `DurationMinutes` parsed from `Duration` when it parses (formats seen: `h:mm`, `hh:mm:ss`, minutes), `LegacyWeather` = `"<weather>; <wind>; <temperature>"` from the lookup names (skip missing parts), `ClientUpdatedAt = ModifiedAt = now`, `TrialCounted = false`.
  - Hole results from `RoundDetail` joined to `Hole` for the number: `EntrySequence = SequenceNumber ?? Hole.Number` (if that duplicates within a round, fall back to detail order and report it), `GreensideSandShots = SandShots`, `FairwaySandShots = 0`, `PenaltyStrokes = PenaltyShots`, other fields one-to-one, `IsDerived = false`.
  - `IsActive = 1` filters apply to `Round` and `RoundDetail` only (and `HandicapHistory` only if it has the column; the live database doesn't), for the chosen `--golfer-id`. Every course, tee and hole an imported round plays is imported whatever its own `IsActive` (golftrack's `Stats_*` procedures don't filter them, and most played tees were deactivated later); inactive tees and courses no imported round plays are not imported. Duplicate `Hole` rows for the same (TeeId, Number): the active one is used, else the lowest Id; duplicates that differ in par, yardage or handicap are reported. A round detail's hole number always comes from its own `HoleId`.
  - Handicap history → `HandicapEntry` (`EffectiveOn` = date of `HandicapDate`).
- **Report, don't fix:** duplicate rounds (same golfer, date, tee and identical per-hole scores) are all imported and listed; rounds whose tee or holes are missing are skipped and listed; holes with `Putts = 0` and `Score > 1` that weren't GIR-from-off-the-green-looking are counted per year (the "Putts = 0 means not entered" question from phase 1) and listed.

## Review Focus

1. **Re-running is safe:** running `import` twice gives exactly the same rows and counts, and touches nothing it didn't create (other accounts, API-imported courses). Test in Task 3.
2. **No secret leakage:** no connection string or password appears in output, exceptions or the report. Test in Task 3.
3. **Mapping edge cases:** null `SequenceNumber`, duplicated sequence numbers, a 9-hole tee, null ratings, odd stroke indexes on 9 holes, unknown round type, unparseable duration, a round with a missing hole, a round that plays a hole twice. Tests in Task 2.
4. **Parity honesty:** the parity report shows every compared number, and separates "intended difference" (the documented phase 1 decisions) from "unexplained difference". Nothing is hidden by tolerance except float rounding (±0.006 on averages/percentages: golftrack rounds to 2 dp). Test in Task 4.

---

## File Structure

```
src/CustomGolfStats.Migration/
  CustomGolfStats.Migration.csproj     references Api (for AppDbContext, entities, CourseImport helper) and Domain
  Program.cs                           System.CommandLine: import, parity
  Source/GolftrackReader.cs            SELECTs into Legacy* records
  Source/LegacyModels.cs               LegacyCourse, LegacyTee, LegacyHole, LegacyRound, LegacyRoundDetail, LegacyHandicap, lookups
  Mapping/GolftrackMapper.cs           pure: legacy records -> entities + MigrationReport entries
  Mapping/DeterministicIds.cs          UUID v5
  Import/Importer.cs                   clear + write in one transaction, dry run
  Parity/LegacyStatsReader.cs          runs golftrack's Stats_* / Round_SeasonSummary procedures per year
  Parity/ParityComparer.cs             legacy numbers vs domain stats; intended-difference rules
  Report/MigrationReport.cs            collects findings; writes Markdown
tests/CustomGolfStats.Migration.Tests/
  Infrastructure/MsSqlFixture.cs       SQL Server 2022 container (linux/amd64), runs 01-tables.sql (+ 03-sprocs.sql for parity tests)
  Infrastructure/LegacySeed.cs         inserts synthetic golftrack rows
  Mapping/GolftrackMapperTests.cs
  Import/ImporterTests.cs              against both containers
  Parity/ParityTests.cs
docs/migration/README.md               how Ron exports a backup, restores it locally, runs import and parity
```

---

### Task 1: Project skeleton, containers and reader

- Create the console and test projects, add them to the slnx, add packages to `Directory.Packages.props`.
- `MsSqlFixture`: `mcr.microsoft.com/mssql/server:2022-latest` with `linux/amd64` platform, random SA password generated in the test, creates a `golftrack` database and runs `docs/reference/golftrack/01-tables.sql` (split on `GO`). Reuse the API test project's Postgres approach for the target (a migrated database per test class).
- `GolftrackReader` reads every table the mapping needs for one golfer (courses/tees/holes reachable from that golfer's rounds, lookups, handicap history). Plain ADO.NET, parameterised.
- Tests: the fixture starts and the schema exists; the reader returns seeded rows for golfer A and nothing from golfer B; inactive rows are excluded.

### Task 2: Mapper and deterministic ids

- `DeterministicIds.For(string table, int id)` → stable UUID v5 (test fixed vectors).
- `GolftrackMapper.Map(LegacyData, Guid accountId, DateTimeOffset now)` → entities plus report findings, applying every mapping rule in the Global Constraints. Pure, no I/O.
- Tests: every bullet in Review Focus 3, plus field-by-field checks of a normal 18-hole round, legacy weather text, handicap entries, round type mapping and duplicate-round detection.

### Task 3: Importer and `import` command

- `import --golfer-id <int> --account-email <email> [--dry-run] [--report <path>]`, connection strings from env/options.
- One transaction: clear previous import (Global Constraints), write courses (skipping ones another account already uses: keep them, just reuse ids), rounds, hole results, handicap entries; commit unless dry run. Writes the Markdown report (counts per year, skipped rows, duplicates, Putts = 0 findings, missing ratings, unknown round types).
- After import, the API's stats endpoints work on the data (assert through `RoundDataLoader` + domain).
- Tests: import into an empty target; second run gives identical counts and ids; another account's rounds and an API-imported course are untouched; dry run commits nothing; missing account → clear error, exit code 2; connection strings never appear in stdout/stderr or the report (inject a recognisable fake password and assert it's absent).

### Task 4: Parity command

- `parity --golfer-id <int> --account-email <email> [--report <path>]`: for each year with rounds, call golftrack's procedures (`Stats_ScoringStats`, `Stats_DrivingStats`, `Stats_ApproachStats`, `Stats_PuttingStats`, `Round_SeasonSummary`, `Stats_CareerStats`, `Stats_CourseStats`; read each one's parameters from `03-sprocs.sql`) and compute the same sections with the domain stats over the imported data; compare field by field.
- Intended differences are rules with a name and a reason, from the roadmap: eagles include albatrosses; driving and GIR-from-fairway/left/right ignore par 3 flags; birdie conversion counts only GIR birdies; sand saves count greenside sand only; the 18-hole histogram has 4 buckets; only exact 9/18-hole rounds count in 9/18 figures. A difference matched by a rule is "intended"; anything else is "unexplained".
- Output: Markdown table per year and section: stat, golftrack, new, difference, verdict.
- Tests: with `03-sprocs.sql` loaded into the test SQL Server and a small seeded history, the report shows matches for plain stats, an "intended" verdict for an albatross case, and an "unexplained" verdict when the test tampers with one imported hole.

### Task 5: Runbook and real run (controller + Ron)

- `docs/migration/README.md`: export a `.bak` (SQL Server) or `.bacpac` (Azure SQL); restore into a local throwaway container (`RESTORE DATABASE ... WITH MOVE`, or `SqlPackage /Action:Import` for bacpac); create the account through the API; run `import --dry-run`, then `import`, then `parity`; delete the container. No real credentials involved anywhere.
- With Ron's backup: the controller runs it locally, shares the parity report, and records findings in the roadmap.

### Task 6: Final review

- Whole-branch review against Review Focus, fixes, roadmap update, push, PR.
