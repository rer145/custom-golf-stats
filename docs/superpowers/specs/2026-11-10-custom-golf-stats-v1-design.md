# Custom Golf Stats — v1 Design

Status: draft for review · Date: 2026-11-10 · Reference app: `golftrack-xyz` (read-only, never modified)

## 1. Purpose

Custom Golf Stats is a rebuild of Ron's personal golftrack app as a product for many golfers, which will eventually be paid and tiered. v1 brings golftrack's features onto a modern stack, adds much easier course creation, and adds shot-by-shot logging. Strokes gained is not calculated in v1, but v1 captures the data it needs, so v2 only adds the calculation.

### Goals for v1

- A golfer can sign up, set up a profile, find or create a course, and log a round from their phone, including on the course with no signal.
- Two entry modes per round: **quick entry** (hole totals, same as golftrack) and **shot-by-shot**.
- Every golftrack stat is reproduced, in the app and on a full-screen web site, behind a login.
- Admins can manage global data: courses, accounts, tiers and roles.
- Ron's golftrack history (2011 onward) is migrated, and the new stats match golftrack's numbers for that data.

### Not in v1

- Strokes gained calculation (data is captured, see §6).
- Payments and subscriptions. Tiers are assigned by hand by an admin.
- Deeper web analysis beyond golftrack's stats (custom date ranges, filters, new charts).
- Importers (GHIN handicap sync and Hole19 are planned for after v1; GameGolf, ShotScope and scorecard scraping are dropped).
- GPS of any kind, social features, friend groups, achievements, custom metrics, PDF export.
- Calculating a World Handicap System index. The handicap index is entered by the golfer or migrated.

## 2. Users, roles and tiers

Each account has two independent attributes:

| Attribute | Values | Controls |
|---|---|---|
| **Role** | `User`, `Admin` | Admins can edit global data (courses, accounts, tiers, roles) and see the admin area. |
| **Tier** | `Free`, `Paid` | Which features the golfer can use. Assigned by an admin in v1. |

**Entitlements.** Features are checked against a single entitlement service in the shared library, never by checking the tier directly in UI code. v1 has one gated feature:

- `ShotByShotEntry`: Paid tier, or Free tier with trial rounds left. The trial allowance is a server setting (default **3 rounds**). A trial round is used up when a shot-by-shot round is *started*.

The API is the source of truth for the entitlement and the remaining trial count. The app caches both so it can start a shot-by-shot round offline. If the cached count was stale, the server still accepts and stores the round, so no data is ever lost, and the count simply goes to zero.

## 3. v1 features

### 3.1 Accounts and profile
- Sign up and sign in with email and password, Sign in with Apple, or Google. Apple sign-in is required by App Store rules once Google is offered.
- Profile: first and last name, email, gender (used to pick default tees), GHIN number (stored for later sync), home course, units (yards by default).
- Handicap history: the golfer enters an index and date. Migrated history comes from golftrack's `HandicapHistory`.

### 3.2 Courses
Structure: **Course → Tee → TeeHole**, same as golftrack, where each tee has its own 18 holes with par, yardage and stroke index. A tee stores total, front-nine and back-nine par, course rating and slope.

Ways to add a course:
1. **Search and import (main path).** Search GolfCourseAPI by name or location from the app or the web site, pick a result, and the API imports that single course with its tees and holes into our database, recording `Source = GolfCourseAPI` and the provider's course ID. Once imported, all reads come from our database and the provider is never called at runtime.
2. **Manual entry** on the web site (and a simpler version in the app) for courses the provider doesn't have.

Data quality, because provider data can be out of date:
- Each course has a status: `Imported`, `UserEdited` or `Verified`, plus `LastVerifiedAt`.
- Any golfer can **flag** a course, tee or hole with a short note. Flags go to the admin queue.
- Admins have a verification screen showing the course next to its tee ratings and slopes for checking against the USGA course rating database by hand. They can fix values and mark the course `Verified`.
- Edits by regular users to a `Verified` course don't change the shared data. They create a flag for an admin to review. Edits to courses that aren't verified go through directly and set the status to `UserEdited`.

GolfCourseAPI terms allow caching data fetched in normal use, but forbid bulk copying and redistribution. We only import a course when a golfer picks it, never in bulk. Provider integration sits behind an `ICourseProvider` interface so we can switch to another provider (such as golfapi.io for coordinates) later.

### 3.3 Logging a round
Round header: course and tee, date played, holes played (9 or 18) and starting hole (1 or 10 for nine-hole rounds), round type, optional weather, wind, temperature, duration, distance walked and notes. These are golftrack's fields, minus the GameGolf and Hole19 links.

The golfer chooses the **entry mode** when they start the round, and it applies to the whole round.

**Quick entry** (golftrack parity), one record per hole:
score, putts, fairway hit, tee shot left, right, long and short (independent flags, as in golftrack), GIR, penalty strokes, sand shots. GIR is suggested automatically from score and putts (`score − putts ≤ par − 2`) and can be overridden.

**Shot-by-shot**: the golfer logs every stroke (see §6). The hole summary record (the same fields as quick entry) is **derived from the shots** and stored, so every stat works the same way for both modes.

Rounds can be edited after they're finished. Changing a shot-by-shot round recalculates its hole summaries.

### 3.4 Stats (golftrack parity)
The same sections and stats as the current golftrack stats page, filtered by year:

- **Overall:** handicap index (as of a date), 18-hole scoring average, holes played, +/- par, with charts for driving accuracy, GIR, scoring percentages, recovery, putting averages and handicap history.
- **Season:** rounds played split into 9 and 18 holes, and score-distribution histograms (9 holes: <40, 40–49, 50+; 18 holes: <80, 80–89, 90–99, 100+).
- **Season results:** one row per round with date, course (tee), holes, score, to par, putts, GIR %, driving accuracy %, par 3/4/5 to par, and a link to the scorecard.
- **Career:** one row per year with 9 and 18 hole rounds, average, low and high, holes, to par, average putts, GIR, driving accuracy, earliest and latest round.
- **Scoring:** scoring average (9 and 18), low and high round, birdies, eagles, par breakers, par 3/4/5 averages, birdie-or-better %.
- **Off the tee:** driving accuracy, left and right miss tendency, average score from fairway, left rough and right rough (split into par 4s and par 5s).
- **Approach and around the green:** GIR %, GIR from fairway, left and right, sand save %, scrambling %, hole-out %.
- **Putting:** putts per GIR, overall putts per hole, 1-putt %, 3-putt avoidance, putts per round (9 and 18), birdie conversion.
- **Courses:** one row per course and tee with the career-style columns plus par 3/4/5 averages.
- **Handicap:** a history table and chart.
- **Scorecard** page per round and **course detail** page.

The rules for each stat are ported from golftrack's `Stats_*`, `Chart_*` and `Round_SeasonSummary` stored procedures in `golftrack-xyz/files/03-sprocs.sql`. For example, sand save means a hole with sand shots scored at par or better, scrambling means par or better on a missed GIR, and a hole-out means zero putts. Those definitions are written down as tests (see §10).

In the mobile app, v1 shows a round summary, the scorecard and a compact season summary (the Overall section). The full set of sections lives on the web site.

### 3.5 Web site (Astro)
One Astro site, rendered on the server:
- **Public:** a basic landing page, sign-in and sign-up.
- **Signed in:** the stats sections in §3.4, round history, scorecards, course search, import and manual entry, profile.
- **`/admin`** (admins only): users (view, set tier and role, disable), courses (edit, verify, merge duplicates), the flag queue, and app settings such as the trial round allowance.

The API enforces the admin role on every admin endpoint. Hiding pages in the web site is never the only protection.

### 3.6 Web version of the app
The Uno app also builds for the browser (WebAssembly) with the same UI. It's **best effort**: if it causes significant extra work at any point, it is dropped from v1 without affecting anything else. It works online only, with no offline storage in the browser.

### 3.7 Migrating golftrack data
A one-time console tool reads golftrack's SQL Server database (or a backup restored locally) and writes into the new Postgres database for Ron's account: courses, tees and holes (status `Verified`), rounds and hole details as quick-entry rounds, handicap history and round metadata. Duplicate rounds are reported, not silently merged (for example, the 2026 season results currently show one round four times). The tool can be re-run safely: it clears and reloads its own previously imported records.

## 4. Architecture

```
 Uno app (iOS, Android, WASM) ──┐                      ┌── GolfCourseAPI
   └ local SQLite + sync        │  HTTPS / JSON         │   (course import only)
                                ├──────────► ASP.NET Core API ──► PostgreSQL
 Astro web site (SSR, Node) ────┘                  │
                                                   └ uses the shared domain library
 Shared C# domain library ── used by both the app and the API
```

| Component | Technology | Notes |
|---|---|---|
| Shared domain library | C# / .NET 10 class library | Enums, value types, hole-summary derivation from shots, GIR rule, stats engine, entitlement rules. No database or UI code. |
| API | ASP.NET Core .NET 10, minimal APIs, EF Core with Npgsql | OpenAPI document generated; typed C# client generated from it for the app. |
| Database | PostgreSQL 17 | Fully relational; JSON columns only for rarely queried extras. |
| Mobile and web app | Uno Platform (C#/XAML, MVUX or MVVM), targets iOS, Android and WebAssembly | Local SQLite through EF Core or sqlite-net. |
| Web site | Astro, server-rendered with the Node adapter, TypeScript | Calls the API from the server; a light chart library for the charts. |
| Auth | ASP.NET Core Identity in the API, bearer access and refresh tokens; Apple and Google sign-in | The app stores tokens in secure storage. The web site keeps them in an encrypted httpOnly cookie on the server side. |

### Repository layout (single repo)

```
src/
  CustomGolfStats.Domain/        shared library
  CustomGolfStats.Api/           ASP.NET Core API + EF Core migrations
  CustomGolfStats.Api.Client/    generated typed client (used by the app)
  CustomGolfStats.App/           Uno app
  CustomGolfStats.Migration/     golftrack → new DB console tool
web/                             Astro site
tests/
  CustomGolfStats.Domain.Tests/
  CustomGolfStats.Api.Tests/     integration tests against real Postgres (Testcontainers)
deploy/                          docker compose, Caddy, backup config
docs/
```

## 5. Data model (main tables)

- **Account**: Identity user plus role, tier, trial shot rounds used, profile fields, home course, units.
- **HandicapEntry**: account, date, index.
- **Course**: name, address, city, state or region, country, phone, website, status, source, provider ID, last verified at.
- **Tee**: course, name, gender, par, rating, slope, front and back nine par, rating and slope.
- **TeeHole**: tee, hole number, par, yardage, stroke index.
- **Round**: client-generated GUID ID, account, tee, played on, holes played, starting hole, entry mode, round type, weather, wind, temperature, duration, distance, notes, updated at, deleted at (soft delete), version.
- **HoleResult** (one per hole played): round, hole number, score, putts, fairway hit, tee shot left, right, long and short, GIR, penalty strokes, sand shots, `IsDerived` (true when it was calculated from shots).
- **Shot** (shot-by-shot rounds only): see §6.
- **CourseFlag**: course, tee and hole (optional), reporter, note, status, resolved by.
- **AppSetting**: key and value (for example the trial round allowance).

Rounds, hole results and shots are owned by one golfer. Courses, tees and holes are shared by everyone.

## 6. The shot model and data for strokes gained

Strokes gained compares each shot's starting position (distance to the hole and lie) with where the ball ended up. So each shot stores **where it started** and **where it ended**, measured as distance remaining to the hole. It doesn't store carry distance. No GPS is used.

**Shot** columns:
| Field | Type | Notes |
|---|---|---|
| RoundId, HoleNumber, Sequence | | Order of strokes on the hole. |
| StartLie | enum | Tee, Fairway, Rough, Sand, Recovery (trees or trouble), Green |
| StartDistance | int | Yards to the hole, or **feet** when StartLie is Green. Filled in automatically from the previous shot's end. |
| Club | enum | Driver, 3–7 wood, 2–6 hybrid, 2–9 iron, PW, GW, SW, LW, Putter. A per-golfer bag comes later. |
| ShotType | enum | Tee, Approach, Layup, Chip, Pitch, Bunker, Recovery, Putt |
| EndLie | enum | StartLie values plus Holed, Penalty area, Out of bounds |
| EndDistance | int? | Yards or feet as above. Null when holed. |
| Direction | enum | OnTarget, Left, Right, Short, Long (with combinations such as ShortLeft) |
| Shape | enum? | Straight, Draw, Fade, Hook, Slice, Push, Pull, plus Topped and Fat as mishits |
| PenaltyStrokes | int | 0, 1 or 2 |

The screens for entering this are meant to be refined later. The columns are what v2 needs.

**Deriving the hole summary from shots** (shared library, used by both the app and the API):
- Score = number of shots + penalty strokes
- Putts = shots whose StartLie is Green
- Fairway hit = on par 4s and 5s, the first shot's EndLie is Fairway. Tee shot left, right, long and short come from its Direction.
- GIR = the ball first reaches the green (or is holed) within par − 2 strokes, counting penalties
- Sand shots = shots whose StartLie is Sand
- Penalty strokes = sum of PenaltyStrokes

## 7. Offline use and sync

- The app keeps its own SQLite database of the golfer's rounds, hole results and shots, plus the courses they've used or marked as favorites. Logging a round never needs a connection.
- IDs for rounds are GUIDs created on the device, so the device can create records without asking the server.
- **Push:** the app sends a whole round (header, hole results, shots) whenever it changes and the device is online. The server keeps whichever version was edited most recently (**last write wins per round**), which is safe because only one golfer owns a round. Deletes are soft deletes so they sync too.
- **Pull:** the app asks for "everything changed since my last sync" and gets rounds, the courses it uses, its handicap entries and its entitlements.
- Course import needs a connection. When offline, a golfer can create a course by hand on the device, and it syncs later.

## 8. API surface (outline)

- `auth/*`: register, log in, refresh, Apple and Google sign-in
- `me`: profile, handicap entries, entitlements
- `courses`: search our database, `courses/provider-search`, `courses/import`, read, create and update, flag
- `rounds`: list, read, upsert (whole round), delete, `sync/changes?since=`
- `stats/{year}/*`: the sections in §3.4, plus `stats/career`, `stats/courses`, `rounds/{id}/scorecard`
- `admin/*`: users, roles, tiers, courses and verification, flags, settings. Admin role required.

## 9. Hosting and operations

- **Development and beta:** Ron's VPS running Docker Compose with the API, the Astro site, Postgres 17 on a persistent volume, and Caddy for HTTPS. Continuous WAL backups with WAL-G to object storage (S3 or Backblaze B2) from day one, with restores tested before beta.
- **Before paying customers:** move Postgres to managed Postgres on Azure or AWS (dump and restore plus a connection string change). The API and web site can stay on the VPS or move as well.
- CI with GitHub Actions: build and test the .NET solution and the Astro site on every push. Mobile store builds are set up near the end of v1.
- Secrets (GolfCourseAPI key, Apple and Google keys, database password) come from environment variables, never committed.

## 10. Testing

- **Domain library:** unit tests for the GIR rule, hole-summary derivation from shots (including penalties, chip-ins and holes with no putts) and every stat.
- **Parity tests:** after migrating Ron's history, the stats for each year are compared with the numbers golftrack produces for the same year. Any difference is either a bug or a documented change in a definition.
- **API:** integration tests against a real Postgres (Testcontainers), covering sync conflicts, entitlement checks, admin authorization and course import (provider faked).
- **App:** view-model tests for round entry. Manual checks on an iOS and an Android device.

## 11. After v1 (for context only)

v2: strokes gained (baseline tables, then calculations per shot and per category), payments (App Store and Play subscriptions, Stripe on the web), deeper web analysis. Later: GHIN handicap sync, Hole19 import, a per-golfer club bag, course coordinates from another provider, social features and achievements.

## 12. Decisions made in planning

| Topic | Decision |
|---|---|
| Product | For many golfers, paid with tiers later; v1 is the free-app basics plus shot logging |
| Backend | .NET 10 API, EF Core, PostgreSQL |
| App | Uno Platform for iOS, Android and the browser, sharing a C# domain library with the API |
| Web site | Astro, separate from the app, everything behind a login, admin area inside it |
| Entry modes | The golfer picks quick or shot-by-shot per round; shot-by-shot is paid with a 3-round trial |
| Payments | Not in v1; tiers set by an admin |
| Courses | GolfCourseAPI import per course, plus manual entry, a verification workflow and user flags |
| Shots | Relational Shot table (not JSON) with start and end distance and lie; no GPS |
| Stats | Same as golftrack for v1, calculated in shared C# rather than stored procedures |
| Hosting | VPS with Docker for dev and beta; managed Postgres before paying customers |
| History | Migrate Ron's golftrack data in v1 |
| Importers | GHIN and Hole19 after v1; the others dropped |
