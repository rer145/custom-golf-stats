# Phase 1: Shared Domain Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `CustomGolfStats.Domain`, the C# library shared by the API and the Uno app. It holds the golf rules (shot validation, deriving hole summaries from shots, GIR, round length, entitlements) and the complete golftrack stats engine, all with no database or UI dependencies.

**Architecture:** Plain .NET 10 class library made of immutable records and static, pure functions. Stats take a list of `RoundData` (rounds with their hole summaries) and return section records whose numbers are `Ratio`/`Average` values, so callers can show both the percentage and the "Additional Stats" counts golftrack shows. Phase 2 maps database entities to these records. Phase 5 maps SQLite rows to them.

**Tech Stack:** .NET 10, C# latest, xUnit v3, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-10-custom-golf-stats-v1-design.md` (sections 2, 3.3, 3.4, 6). Roadmap: `docs/superpowers/plans/2026-11-11-v1-roadmap.md`.

## Global Constraints

- Target `net10.0`; `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>` for all projects via `Directory.Build.props`.
- `CustomGolfStats.Domain` has **no package references** (it must compile for iOS, Android and WebAssembly through Uno).
- Tests use xUnit v3 and plain `Assert`. No FluentAssertions (commercial license).
- Distances: `Shot.StartDistance`/`EndDistance` are **yards**, except **feet** when the lie is `Green`.
- Penalty strokes: any integer ≥ 0, no upper limit.
- A round may have any number of holes. 9/18 figures include only rounds with exactly 9 or exactly 18 hole summaries. All other per-hole stats include every hole.
- Sand saves count **greenside** sand only.
- Stat definitions: port from `docs/reference/golftrack/03-sprocs.sql`. Where this plan and a stored procedure disagree, the procedure wins, *except* for the three rules above (round length, greenside sand, uncapped penalties).
- Percentages are never computed as NaN. A zero denominator gives `Value == null`.

## Review Focus

1. **A year (or course) with no rounds, or no par 3s, or no GIRs:** every `Ratio`/`Average` has `Value == null` and nothing throws. Tests in Tasks 5–9.
2. **Rounds that aren't 9 or 18 holes (for example 12, or 18 with a hole played twice):** left out of 9/18 figures, included in per-hole stats and in season results. Tests in Tasks 1, 5 and 9.
3. **Penalties and re-tees:** a tee shot out of bounds (1 penalty) followed by a second shot from the `Tee` lie. Score counts both shots plus the penalty, fairway hit uses only the first shot, and GIR counts the penalty. Large penalty counts (for example 4) work. Tests in Tasks 2 and 3.
4. **Holing out from off the green and holes-in-one:** a chip-in has 0 putts, and a par 3 ace is a GIR (1 ≤ par − 2). Tests in Task 3.
5. **A putt from the fringe** (putter, StartLie `Fairway`/`Rough`) is not counted as a putt; only shots that start on `Green` count. Test in Task 3.

---

## File Structure

```
Directory.Build.props
CustomGolfStats.slnx
.github/workflows/ci.yml
src/CustomGolfStats.Domain/
  CustomGolfStats.Domain.csproj
  Enums.cs                     Lie, Club, ShotType, Direction, Shape, EntryMode, Role, Tier, Feature, RoundLength
  Holes/HoleInfo.cs            HoleInfo record
  Holes/HoleSummary.cs         HoleSummary record
  Holes/GirRule.cs             GIR suggestion for quick entry
  Holes/RoundLengthRule.cs     9/18 classification
  Shots/Shot.cs                Shot record
  Shots/ShotValidator.cs       ShotError enum + Validate
  Shots/HoleSummaryDeriver.cs  shots -> HoleSummary
  Entitlements/Entitlements.cs feature gating + trial rounds
  Stats/Measures.cs            Ratio, Average
  Stats/RoundData.cs           RoundData record + per-round helpers
  Stats/ScoringStats.cs        Scoring section
  Stats/OffTheTeeStats.cs      Off the tee section
  Stats/ApproachStats.cs       Approach / around the green section
  Stats/PuttingStats.cs        Putting section
  Stats/SeasonStats.cs         Overall tiles, season counts, histograms, season result rows
  Stats/CareerStats.cs         Career-by-year and course/tee tables
tests/CustomGolfStats.Domain.Tests/
  CustomGolfStats.Domain.Tests.csproj
  Builders.cs                  test helpers: Hole(...), Round(...)
  (one test file per source file above)
```

Everything is in namespace `CustomGolfStats.Domain` (sub-namespaces by folder: `.Holes`, `.Shots`, `.Entitlements`, `.Stats`).

---

### Task 1: Solution skeleton, CI, enums and simple rules

**Files:**
- Create: `Directory.Build.props`, `CustomGolfStats.slnx`, `.github/workflows/ci.yml`, `.gitignore` (dotnet template)
- Create: `src/CustomGolfStats.Domain/CustomGolfStats.Domain.csproj`, `Enums.cs`, `Holes/HoleInfo.cs`, `Holes/HoleSummary.cs`, `Holes/GirRule.cs`, `Holes/RoundLengthRule.cs`
- Create: `tests/CustomGolfStats.Domain.Tests/CustomGolfStats.Domain.Tests.csproj`, `Builders.cs`, `Holes/GirRuleTests.cs`, `Holes/RoundLengthRuleTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum Lie { Tee, Fairway, Rough, FairwaySand, GreensideSand, Recovery, Green, Holed, PenaltyArea, OutOfBounds }
  public enum Club { Driver, Wood3, Wood4, Wood5, Wood7, Hybrid2, Hybrid3, Hybrid4, Hybrid5, Hybrid6,
                     Iron2, Iron3, Iron4, Iron5, Iron6, Iron7, Iron8, Iron9,
                     PitchingWedge, GapWedge, SandWedge, LobWedge, Putter }
  public enum ShotType { Tee, Approach, Layup, Chip, Pitch, Bunker, Recovery, Putt }
  public enum Direction { OnTarget, Left, Right, Short, Long, ShortLeft, ShortRight, LongLeft, LongRight }
  public enum Shape { Straight, Draw, Fade, Hook, Slice, Push, Pull, Topped, Fat }
  public enum EntryMode { Quick, ShotByShot }
  public enum Role { User, Admin }
  public enum Tier { Free, Paid }
  public enum Feature { ShotByShotEntry }
  public enum RoundLength { Nine, Eighteen }

  public sealed record HoleInfo(int Number, int Par, int Yardage, int StrokeIndex);
  public sealed record HoleSummary(int HoleNumber, int EntrySequence, int Par, int Score, int Putts,
      bool FairwayHit, bool TeeShotLeft, bool TeeShotRight, bool TeeShotLong, bool TeeShotShort,
      bool Gir, int PenaltyStrokes, int GreensideSandShots, int FairwaySandShots)
  { public int ToPar => Score - Par; }

  public static class GirRule { public static bool Suggest(int par, int score, int putts); }
  public static class RoundLengthRule { public static RoundLength? Classify(int holeCount); }
  ```
- Test helper `Builders.Hole(int par, int score, int putts = 2, bool fairway = false, bool gir = false, bool left = false, bool right = false, bool shortMiss = false, bool longMiss = false, int penalties = 0, int greensideSand = 0, int fairwaySand = 0, int number = 1)` returns a `HoleSummary` (EntrySequence = number).

- [ ] **Step 1: Scaffold.** `dotnet new classlib`, `dotnet new xunit3` (xUnit v3 template), `dotnet new sln --format slnx`, add both projects, test project references Domain. `Directory.Build.props` holds the Global Constraints settings. CI workflow: on push and pull_request, `actions/setup-dotnet` with `10.0.x`, then `dotnet test CustomGolfStats.slnx --configuration Release`.

- [ ] **Step 2: Write failing tests**

```csharp
// GirRuleTests
[Theory]
[InlineData(4, 4, 2, true)]   // reached in 2
[InlineData(4, 5, 2, false)]  // reached in 3
[InlineData(3, 1, 0, true)]   // ace
[InlineData(5, 4, 1, true)]   // reached in 3 on a par 5
[InlineData(4, 4, 0, false)]  // holed out from 4th shot, never on green in regulation
public void Suggest_uses_score_minus_putts(int par, int score, int putts, bool expected)
    => Assert.Equal(expected, GirRule.Suggest(par, score, putts));

// RoundLengthRuleTests
[Theory]
[InlineData(9, RoundLength.Nine)]
[InlineData(18, RoundLength.Eighteen)]
public void Classify_exact_lengths(int holes, RoundLength expected) => Assert.Equal(expected, RoundLengthRule.Classify(holes));

[Theory] [InlineData(0)] [InlineData(1)] [InlineData(12)] [InlineData(19)] [InlineData(27)]
public void Classify_other_lengths_is_null(int holes) => Assert.Null(RoundLengthRule.Classify(holes));
```

- [ ] **Step 3: Run** `dotnet test`. Expected: compile failure (types missing).
- [ ] **Step 4: Implement** the enums, records and the two rules (`score - putts <= par - 2`; 9 → Nine, 18 → Eighteen, else null).
- [ ] **Step 5: Run** `dotnet test`. Expected: all pass, 0 warnings.
- [ ] **Step 6: Commit** `feat(domain): solution skeleton, enums, GIR and round length rules`

---

### Task 2: Shot record and validation

**Files:**
- Create: `src/CustomGolfStats.Domain/Shots/Shot.cs`, `Shots/ShotValidator.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Shots/ShotValidatorTests.cs`

**Interfaces:**
- Consumes: `Lie`, `Club`, `ShotType`, `Direction`, `Shape` (Task 1)
- Produces:
  ```csharp
  public sealed record Shot(int Sequence, Lie StartLie, int StartDistance, Club Club, ShotType Type,
      Lie EndLie, int? EndDistance, Direction Direction, Shape? Shape, int PenaltyStrokes);

  public enum ShotError { Empty, SequenceNotContiguous, InvalidStartLie, HoledBeforeLastShot,
                          NotHoled, EndDistanceMismatch, NegativeDistance, NegativePenalty }

  public static class ShotValidator
  {
      // Errors for a hole's shots; empty list = valid and complete.
      public static IReadOnlyList<ShotError> Validate(IReadOnlyList<Shot> shots);
  }
  ```
- Test helper added to `Builders.cs`: `Builders.Shot(int seq, Lie start, int startDist, Lie end, int? endDist, Club club = Club.Iron7, ShotType type = ShotType.Approach, Direction dir = Direction.OnTarget, int penalties = 0)`.

Rules (each error reported at most once):
- `Empty`: no shots.
- `SequenceNotContiguous`: sequences sorted aren't exactly 1..n.
- `InvalidStartLie`: any StartLie is `Holed`, `PenaltyArea` or `OutOfBounds` (the next shot after a penalty starts from wherever the golfer drops or re-tees).
- `HoledBeforeLastShot`: a shot other than the last has EndLie `Holed`.
- `NotHoled`: last shot's EndLie isn't `Holed` (the hole isn't finished yet; the app shows it as in progress).
- `EndDistanceMismatch`: EndDistance is null when EndLie isn't `Holed`, or not null when it is.
- `NegativeDistance` / `NegativePenalty`: any value < 0.

- [ ] **Step 1: Write failing tests**: `Valid_par4_two_putt_has_no_errors`, `Empty_list_returns_Empty`, `Gap_in_sequence_returns_SequenceNotContiguous` (sequences 1,3), `Holed_in_middle_returns_HoledBeforeLastShot`, `Unfinished_hole_returns_NotHoled`, `Holed_with_distance_returns_EndDistanceMismatch`, `Negative_penalty_returns_NegativePenalty`, `Start_from_penalty_area_returns_InvalidStartLie`, and `Out_of_bounds_retee_with_four_penalty_strokes_is_valid` (tee → OutOfBounds 1 penalty, tee → OutOfBounds 1 penalty, tee → Fairway, …holed; and a single shot with `PenaltyStrokes = 4` validates). Each asserts the exact `ShotError` list with `Assert.Equal(new[] { ShotError.X }, result)`.
- [ ] **Step 2: Run** `dotnet test --filter ShotValidatorTests`. Expected: compile failure.
- [ ] **Step 3: Implement** `Shot` and `ShotValidator.Validate`.
- [ ] **Step 4: Run** the same filter. Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): shot record and validation`

---

### Task 3: Derive a hole summary from shots

**Files:**
- Create: `src/CustomGolfStats.Domain/Shots/HoleSummaryDeriver.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Shots/HoleSummaryDeriverTests.cs`

**Interfaces:**
- Consumes: `Shot`, `ShotValidator` (Task 2); `HoleInfo`, `HoleSummary` (Task 1)
- Produces:
  ```csharp
  public static class HoleSummaryDeriver
  {
      // Throws ArgumentException listing the ShotErrors if ShotValidator.Validate(shots) is not empty.
      public static HoleSummary Derive(HoleInfo hole, int entrySequence, IReadOnlyList<Shot> shots);
  }
  ```

Rules (spec §6). Shots are processed in Sequence order:
- Score = shot count + sum of PenaltyStrokes.
- Putts = shots with StartLie `Green`.
- FairwayHit = par ≥ 4 and the first shot's EndLie is `Fairway`.
- TeeShotLeft/Right/Short/Long come from the **first** shot's Direction, on par ≥ 4 only (all false on par 3, matching golftrack's driving holes). Left = Left, ShortLeft, LongLeft. Right = Right, ShortRight, LongRight. Short = Short, ShortLeft, ShortRight. Long = Long, LongLeft, LongRight.
- Gir: walk shots, adding `1 + PenaltyStrokes` to a running count. At the first shot whose EndLie is `Green` or `Holed`, Gir = running count ≤ par − 2. A valid hole always ends `Holed`, so this always resolves.
- GreensideSandShots = shots with StartLie `GreensideSand`; FairwaySandShots = StartLie `FairwaySand`.
- PenaltyStrokes = sum.

- [ ] **Step 1: Write failing tests** (each builds shots with `Builders.Shot` and asserts every changed field of the returned `HoleSummary`):
  - `Par4_fairway_green_two_putts`: Score 4, Putts 2, FairwayHit true, Gir true, all miss flags false.
  - `Par4_tee_left_rough_then_green`: FairwayHit false, TeeShotLeft true, Gir true.
  - `Tee_shot_out_of_bounds_then_retee` (tee → OutOfBounds, 1 penalty; tee → Fairway; → Green; 2 putts): Score 6, PenaltyStrokes 1, FairwayHit false (first shot only), Gir false (running count 4 > 2).
  - `Large_penalty_count` (a shot with PenaltyStrokes 4): Score includes all 4.
  - `Chip_in_from_rough`: Putts 0; Gir false when holed on stroke 3 of a par 4.
  - `Par3_hole_in_one`: Score 1, Putts 0, Gir true, FairwayHit false, miss flags false.
  - `Putt_from_fringe_is_not_a_putt` (Club.Putter from StartLie Fairway, StartDistance 10 yards, ends Green; then 1 putt from Green): Putts 1.
  - `Greenside_and_fairway_sand_counted_separately`: one FairwaySand shot + two GreensideSand shots → 1 and 2.
  - `Invalid_shots_throw`: unfinished hole → `ArgumentException`.
  - `EntrySequence_and_number_copied`: HoleNumber = hole.Number, EntrySequence as passed, Par = hole.Par.
- [ ] **Step 2: Run** `dotnet test --filter HoleSummaryDeriverTests`. Expected: compile failure.
- [ ] **Step 3: Implement** `HoleSummaryDeriver.Derive`.
- [ ] **Step 4: Run** the filter. Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): derive hole summary from shots`

---

### Task 4: Entitlements

**Files:**
- Create: `src/CustomGolfStats.Domain/Entitlements/Entitlements.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Entitlements/EntitlementsTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class Entitlements
  {
      public const int DefaultTrialShotRounds = 3;
      public static bool CanUse(Feature feature, Tier tier, int trialRoundsUsed, int trialAllowance);
      // null = unlimited (Paid). Never negative.
      public static int? TrialRoundsRemaining(Tier tier, int trialRoundsUsed, int trialAllowance);
  }
  ```

- [ ] **Step 1: Write failing tests**: `Paid_can_always_use_shot_entry` (used 50, allowance 3 → true; remaining null); `Free_with_trial_left_can_use` (used 2 of 3 → true, remaining 1); `Free_with_trial_used_up_cannot_use` (used 3 of 3 → false, remaining 0); `Over_used_trial_reports_zero_not_negative` (used 5 of 3 → remaining 0); `Zero_allowance_blocks_free` (0 of 0 → false).
- [ ] **Step 2: Run** `dotnet test --filter EntitlementsTests`. Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): entitlements and trial rounds`

---

### Task 5: Stats foundation and the Scoring section

**Files:**
- Create: `src/CustomGolfStats.Domain/Stats/Measures.cs`, `Stats/RoundData.cs`, `Stats/ScoringStats.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Stats/MeasuresTests.cs`, `Stats/ScoringStatsTests.cs`
- Reference: `docs/reference/golftrack/03-sprocs.sql` → `Stats_ScoringStats` (line 3496), `Stats_RelationToPar` (3406)

**Interfaces:**
- Produces:
  ```csharp
  public readonly record struct Ratio(int Numerator, int Denominator)
  { public double? Value => Denominator == 0 ? null : (double)Numerator / Denominator; }
  public readonly record struct Average(int Total, int Count)
  { public double? Value => Count == 0 ? null : (double)Total / Count; }

  public sealed record RoundData(Guid Id, DateOnly PlayedOn, Guid CourseId, string CourseName,
      Guid TeeId, string TeeName, IReadOnlyList<HoleSummary> Holes)
  {
      public int Score => Holes.Sum(h => h.Score);
      public int Par => Holes.Sum(h => h.Par);
      public int ToPar => Score - Par;
      public int Putts => Holes.Sum(h => h.Putts);
      public RoundLength? Length => RoundLengthRule.Classify(Holes.Count);
  }

  public sealed record ScoringSection(
      Average ScoringAverage9, Average ScoringAverage18,
      int? Lowest9, int? Highest9, int? Lowest18, int? Highest18,
      int Birdies, int Eagles,                // eagles = to par <= -2 (albatross counts as eagle)
      Ratio ParBreakers,                      // (birdies + eagles) / holes
      Average Par3Average, Average Par4Average, Average Par5Average,  // strokes / holes of that par
      Ratio BirdieOrBetter);                  // (birdies + eagles) / holes

  public static class ScoringStats { public static ScoringSection Calculate(IReadOnlyList<RoundData> rounds); }
  ```
- Test helper added to `Builders.cs`: `Builders.Round(DateOnly playedOn, params HoleSummary[] holes)` (fixed course/tee IDs unless overridden with `Builders.Round(playedOn, courseId, teeId, holes)`), and `Builders.Holes(int count, int par, int score)` for uniform rounds.

The 9/18 fields use only rounds whose `Length` is `Nine`/`Eighteen`. Everything else uses every hole of every round. Check `Stats_ScoringStats` for ParBreakers vs BirdieOrBetter; if the sproc defines them differently, follow the sproc and update the comment.

- [ ] **Step 1: Write failing tests**:
  - `MeasuresTests.Zero_denominator_is_null` (`new Ratio(0,0).Value` and `new Average(0,0).Value` are null) and `Ratio_value` (`new Ratio(1,4).Value == 0.25`).
  - `ScoringStats.Empty_input_has_null_values_and_zero_counts`.
  - `Averages_split_by_round_length`: two 18-hole rounds scoring 80 and 90, one 9-hole round of 45 → `ScoringAverage18.Value == 85`, `ScoringAverage9.Value == 45`, `Lowest18 == 80`, `Highest18 == 90`.
  - `Twelve_hole_round_excluded_from_9_18_but_counted_per_hole`: one 12-hole round with one birdie → `ScoringAverage18.Count == 0`, `ScoringAverage9.Count == 0`, `Birdies == 1`, `ParBreakers.Denominator == 12`.
  - `Eighteen_entries_with_a_repeated_hole_count_as_18`: 18 summaries where hole 7 appears twice (17 distinct numbers) → included in `ScoringAverage18`.
  - `Par_averages`: par 3 scores 3 and 4 → `Par3Average.Value == 3.5`. No par 5s → `Par5Average.Value == null`.
- [ ] **Step 2: Run** `dotnet test --filter "MeasuresTests|ScoringStatsTests"`. Expected: compile failure.
- [ ] **Step 3: Implement** `Measures.cs`, `RoundData.cs`, `ScoringStats.Calculate`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): stats measures and scoring section`

---

### Task 6: Off the Tee section

**Files:**
- Create: `src/CustomGolfStats.Domain/Stats/OffTheTeeStats.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Stats/OffTheTeeStatsTests.cs`
- Reference: `Stats_DrivingStats` (line 2834)

**Interfaces:**
- Consumes: `RoundData`, `Ratio`, `Average` (Task 5)
- Produces:
  ```csharp
  public sealed record OffTheTeeSection(
      Ratio DrivingAccuracy,      // fairways hit / driving holes (par != 3)
      Ratio LeftTendency,         // tee shot left / driving holes
      Ratio RightTendency,        // tee shot right / driving holes
      Average Par4ToParFromFairway, Average Par4ToParFromLeft, Average Par4ToParFromRight,
      Average Par5ToParFromFairway, Average Par5ToParFromLeft, Average Par5ToParFromRight); // sum of ToPar / holes
  public static class OffTheTeeStats { public static OffTheTeeSection Calculate(IReadOnlyList<RoundData> rounds); }
  ```

- [ ] **Step 1: Write failing tests**: `Par3s_are_not_driving_holes` (2 par 4s, 1 fairway; 1 par 3 → DrivingAccuracy = 1/2); `Left_and_right_tendency`; `Score_from_fairway_on_par4` (par 4 fairway scores 4 and 5 → `Par4ToParFromFairway.Value == 0.5`); `No_par5s_gives_null`; `Empty_input_all_null`.
- [ ] **Step 2: Run** `dotnet test --filter OffTheTeeStatsTests`. Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): off the tee stats`

---

### Task 7: Approach and Around the Green section

**Files:**
- Create: `src/CustomGolfStats.Domain/Stats/ApproachStats.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Stats/ApproachStatsTests.cs`
- Reference: `Stats_ApproachStats` (line 2350), `Chart_Recovery` (201)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ApproachSection(
      Ratio Gir,               // GIR holes / holes
      Ratio GirFromFairway,    // GIR & fairway hit / fairway-hit holes
      Ratio GirFromLeft,       // GIR & tee left / tee-left holes
      Ratio GirFromRight,      // GIR & tee right / tee-right holes
      Ratio SandSave,          // greenside sand > 0 & ToPar <= 0 / holes with greenside sand > 0
      Ratio Scrambling,        // !GIR & ToPar <= 0 / !GIR holes
      Ratio HoleOut);          // Putts == 0 / holes
  public static class ApproachStats { public static ApproachSection Calculate(IReadOnlyList<RoundData> rounds); }
  ```

- [ ] **Step 1: Write failing tests**: `Gir_ratio`; `Gir_from_fairway_left_right`; `Sand_save_ignores_fairway_bunkers` (one hole with only fairway sand at par, one with greenside sand at par, one with greenside sand at bogey → SandSave = 1/2); `Scrambling_counts_par_or_better_on_missed_greens`; `Hole_out_is_zero_putts`; `Empty_input_all_null`.
- [ ] **Step 2: Run** `dotnet test --filter ApproachStatsTests`. Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): approach and around the green stats`

---

### Task 8: Putting section

**Files:**
- Create: `src/CustomGolfStats.Domain/Stats/PuttingStats.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Stats/PuttingStatsTests.cs`
- Reference: `Stats_PuttingStats` (line 3231), `Chart_PuttingPerHole` (163)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record PuttingSection(
      Average PuttsPerGir,          // putts on GIR holes / GIR holes
      Average PuttsPerHole,         // all putts / holes
      Ratio OnePutt,                // Putts == 1 / holes
      Ratio ThreePuttAvoidance,     // Putts >= 3 / holes (displayed as golftrack does; check sproc)
      Average PuttsPerRound9, Average PuttsPerRound18,   // exact-length rounds only
      Ratio BirdieConversion);      // GIR & ToPar <= -1 / GIR holes
  public static class PuttingStats { public static PuttingSection Calculate(IReadOnlyList<RoundData> rounds); }
  ```

- [ ] **Step 1: Write failing tests**: `Putts_per_gir_only_counts_gir_holes`; `One_and_three_putts`; `Putts_per_round_split_by_length` (an 18-hole round with 32 putts and a 12-hole round → `PuttsPerRound18.Value == 32`, 12-hole round not in either); `Birdie_conversion`; `No_gir_gives_null`.
- [ ] **Step 2: Run** `dotnet test --filter PuttingStatsTests`. Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): putting stats`

---

### Task 9: Overall, Season and Season Results

**Files:**
- Create: `src/CustomGolfStats.Domain/Stats/SeasonStats.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Stats/SeasonStatsTests.cs`
- Reference: `Round_SeasonSummary` (line 2261), `Stats_HolesPlayed` (3152), `Stats_ScoresByType` (3453)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record OverallTiles(Average ScoringAverage18, int HolesPlayed, int ToPar);
  public sealed record ScoreBucket(string Label, int Count);
  public sealed record SeasonSection(int Rounds9, int Rounds18, int RoundsOtherLength,
      IReadOnlyList<ScoreBucket> Histogram9,    // "<40", "40-49", "50+"
      IReadOnlyList<ScoreBucket> Histogram18);  // "<80", "80-89", "90-99", "100+"
  public sealed record SeasonResultRow(Guid RoundId, DateOnly PlayedOn, string CourseName, string TeeName,
      int HoleCount, int Score, int ToPar, int Putts, Ratio Gir, Ratio DrivingAccuracy,
      int? Par3ToPar, int? Par4ToPar, int? Par5ToPar);  // null when the round has no hole of that par
  public static class SeasonStats
  {
      public static IReadOnlyList<RoundData> ForYear(IEnumerable<RoundData> rounds, int year);
      public static OverallTiles Overall(IReadOnlyList<RoundData> rounds);
      public static SeasonSection Season(IReadOnlyList<RoundData> rounds);
      public static IReadOnlyList<SeasonResultRow> Results(IReadOnlyList<RoundData> rounds); // newest first
  }
  ```
- The handicap tile and history aren't calculated here. Phase 2 reads them from handicap entries.

- [ ] **Step 1: Write failing tests**: `ForYear_filters_by_played_on_year`; `Overall_holes_and_to_par_include_odd_length_rounds`; `Histogram_bucket_edges` (18-hole scores 79, 80, 89, 90, 99, 100 → counts 1,2,2,1; 9-hole 39, 40, 49, 50 → 1,2,1); `Odd_length_round_counted_in_RoundsOtherLength_only`; `Results_newest_first_with_per_par_to_par`; `Results_par5_null_when_round_has_no_par5`.
- [ ] **Step 2: Run** `dotnet test --filter SeasonStatsTests`. Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(domain): overall, season and season results`

---

### Task 10: Career and Courses tables

**Files:**
- Create: `src/CustomGolfStats.Domain/Stats/CareerStats.cs`
- Test: `tests/CustomGolfStats.Domain.Tests/Stats/CareerStatsTests.cs`
- Reference: `Stats_CareerStats` (line 2616), `Stats_CourseStats` (2746), `Stats_CareerStatsByCourse` (2679)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record LengthSummary(int Rounds, Average Average, int? Low, int? High); // exact-length rounds only
  public sealed record CareerRow(int Year, LengthSummary Nine, LengthSummary Eighteen, int Holes, int ToPar,
      Average PuttsPerHole, Ratio Gir, Ratio DrivingAccuracy, DateOnly Earliest, DateOnly Latest);
  public sealed record CourseRow(Guid CourseId, string CourseName, Guid TeeId, string TeeName,
      LengthSummary Nine, LengthSummary Eighteen, int Holes, int ToPar, Average PuttsPerHole,
      Ratio Gir, Ratio DrivingAccuracy, Average Par3ToPar, Average Par4ToPar, Average Par5ToPar,
      DateOnly Earliest, DateOnly Latest);
  public static class CareerStats
  {
      public static IReadOnlyList<CareerRow> ByYear(IReadOnlyList<RoundData> allRounds);      // newest year first
      public static IReadOnlyList<CourseRow> ByCourseAndTee(IReadOnlyList<RoundData> rounds); // ordered by course name, then tee name
  }
  ```
- Golftrack listed courses with zero rounds in the year. That's a Phase 2 concern (it knows the course list). Here, only courses with rounds appear.

- [ ] **Step 1: Write failing tests**: `One_row_per_year_newest_first`; `Length_summary_low_high_only_exact_lengths`; `Earliest_and_latest_dates`; `Course_rows_grouped_by_course_and_tee` (same course, two tees → 2 rows); `Course_par_averages_to_par`; `Empty_input_returns_empty_lists`.
- [ ] **Step 2: Run** `dotnet test --filter CareerStatsTests`. Expected: compile failure.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test` (whole solution). Expected: all pass, 0 warnings.
- [ ] **Step 5: Commit** `feat(domain): career and course tables`

---

## Done when

- `dotnet test CustomGolfStats.slnx` passes locally and in GitHub Actions.
- Every Review Focus item has a passing test.
- `CustomGolfStats.Domain.csproj` has no package references.
- Next: write the Phase 2 plan (API and database) from the roadmap, using the type names above.
