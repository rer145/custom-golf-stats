using System.Globalization;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Stats;
using CustomGolfStats.Migration.Mapping;
using CustomGolfStats.Migration.Source;

namespace CustomGolfStats.Migration.Parity;

/// <summary>golftrack's result sets for one year (Stats_CareerStats holds every year; the year's row is used).</summary>
public sealed record LegacyYearResults(
    LegacyResult Scoring,
    LegacyResult Driving,
    LegacyResult Approach,
    LegacyResult Putting,
    LegacyResult Season,
    LegacyResult Career,
    LegacyResult Courses);

/// <summary>The golftrack rows behind the imported rounds: rounds by imported round id, and tees by golftrack id.</summary>
public sealed record ParityLegacyRounds(IReadOnlyDictionary<Guid, LegacyRound> Rounds, IReadOnlyDictionary<int, LegacyTee> Tees)
{
    public static readonly ParityLegacyRounds None = new(new Dictionary<Guid, LegacyRound>(), new Dictionary<int, LegacyTee>());
}

/// <summary>
/// Compares golftrack's numbers with the new domain stats over the imported rounds of one year, column by column.
/// <list type="bullet">
/// <item><b>Match</b>: equal. Counts, to-par and dates exactly; averages and percentages (as 0-100) within ±0.006,
/// which only absorbs golftrack's rounding to 2 decimals (averages) or 4 decimals of a fraction (percentages).</item>
/// <item><b>Intended</b>: different, and both sides are explained by the row's rules. golftrack's value must equal
/// golftrack's definition recomputed here on the imported data (<see cref="Spec.LegacyDefinition"/>), and the new value
/// must equal the rules' definition recomputed here independently of the domain (<see cref="Spec.RuleDefinition"/>).
/// So a rule excuses neither a data difference nor a domain bug.</item>
/// <item><b>Unexplained</b>: any other difference, including a missing golftrack row.</item>
/// <item><b>Not compared</b>: golftrack columns without a counterpart, and values that can't be read as numbers.</item>
/// <item><b>Missing</b>: a column missing in golftrack, or a failed procedure.</item>
/// </list>
/// Pure: no I/O.
/// </summary>
public static class ParityComparer
{
    public const double Tolerance = 0.006;

    private const double Exact = 1e-9;
    private const string ComputedHere = "new value computed by the parity tool (no domain stat)";

    private static readonly HashSet<string> IdentityColumns = new(StringComparer.OrdinalIgnoreCase) { "Year", "Id", "CourseName", "TeeName" };
    private static readonly HashSet<string> LengthColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "RoundsPlayed9", "RoundsPlayed18", "ScoringAvg9", "ScoringAvg18", "Low9HoleScore", "Low18HoleScore", "High9HoleScore", "High18HoleScore",
    };

    public static IReadOnlyList<ParitySection> Compare(
        int year, IReadOnlyList<RoundData> rounds, ParityLegacyRounds legacy, LegacyYearResults results, int otherGolfersRounds = 0)
    {
        var data = new Rounds(rounds, legacy);
        var others = otherGolfersRounds > 0
            ? $"golftrack's numbers in this section include {otherGolfersRounds} active rounds of other golfers (the procedure takes only a year)."
            : null;
        var sections = new List<ParitySection>
        {
            Section("Scoring", results.Scoring, results.Scoring.Rows.FirstOrDefault(), ScoringSpecs(data), data.Empty, others),
            Section("Off the tee", results.Driving, results.Driving.Rows.FirstOrDefault(), DrivingSpecs(data), data.Empty, others),
            Section("Approach", results.Approach, results.Approach.Rows.FirstOrDefault(), ApproachSpecs(data), data.Empty, others),
            Section("Putting", results.Putting, results.Putting.Rows.FirstOrDefault(), PuttingSpecs(data), data.Empty, others),
            Section("Season", results.Season, results.Season.Rows.FirstOrDefault(), SeasonSpecs(data), data.Empty, others),
            Section(
                "Career (this year)",
                results.Career,
                results.Career.Rows.FirstOrDefault(r => Number(r.GetValueOrDefault("Year")) == year),
                CareerSpecs(data),
                data.Empty),
        };
        sections.AddRange(CourseSections(results.Courses, data));
        return sections;
    }

    /// <summary>The verdict for one value. <paramref name="rules"/> empty: Match or Unexplained only.</summary>
    public static Verdict Judge(
        double? golftrack, double? newValue, ValueKind kind, IReadOnlyCollection<IntendedRule> rules, double? legacyDefinition, double? ruleDefinition)
    {
        if (Same(golftrack, newValue, kind))
        {
            return Verdict.Match;
        }

        return rules.Count > 0 && Same(golftrack, legacyDefinition, kind) && Same(newValue, ruleDefinition, kind)
            ? Verdict.Intended
            : Verdict.Unexplained;
    }

    // ---- Year sections ----

    private static IReadOnlyList<Spec> ScoringSpecs(Rounds d)
    {
        var s = ScoringStats.Calculate(d.All);
        IntendedRule[] albatross = [IntendedRules.EaglesIncludeAlbatrosses];
        IntendedRule[] length = [IntendedRules.ExactRoundLength];
        return
        [
            Plain("TotalHoles", ValueKind.Count, d.Holes.Count, ComputedHere),
            Plain("TotalPar3Holes", ValueKind.Count, s.Par3Average.Count),
            Plain("TotalPar4Holes", ValueKind.Count, s.Par4Average.Count),
            Plain("TotalPar5Holes", ValueKind.Count, s.Par5Average.Count),
            Ruled("Total9HoleRounds", ValueKind.Count, s.ScoringAverage9.Count, length, d.ByHolesPlayed(9).Count, d.ByLength(9).Count),
            Ruled("Total18HoleRounds", ValueKind.Count, s.ScoringAverage18.Count, length, d.ByHolesPlayed(18).Count, d.ByLength(18).Count),
            Plain("Birdies", ValueKind.Count, s.Birdies),
            Ruled("Eagles", ValueKind.Count, s.Eagles, albatross, d.Holes.Count(h => ToPar(h) == -2), d.Holes.Count(h => ToPar(h) <= -2)),
            Ruled("BirdiesAndEagles", ValueKind.Count, s.Birdies + s.Eagles, albatross,
                d.Holes.Count(h => ToPar(h) is -1 or -2), d.Holes.Count(h => ToPar(h) <= -1)),
            Plain("TotalPar3Score", ValueKind.Count, s.Par3Average.Total),
            Plain("TotalPar4Score", ValueKind.Count, s.Par4Average.Total),
            Plain("TotalPar5Score", ValueKind.Count, s.Par5Average.Total),
            Ruled("Low9HoleRound", ValueKind.Count, s.Lowest9 ?? 0, length, MinOrZero(d.ByHolesPlayed(9)), MinOrZero(d.ByLength(9))),
            Ruled("High9HoleRound", ValueKind.Count, s.Highest9 ?? 0, length, MaxOrZero(d.ByHolesPlayed(9)), MaxOrZero(d.ByLength(9))),
            Ruled("Low18HoleRound", ValueKind.Count, s.Lowest18 ?? 0, length, MinOrZero(d.ByHolesPlayed(18)), MinOrZero(d.ByLength(18))),
            Ruled("High18HoleRound", ValueKind.Count, s.Highest18 ?? 0, length, MaxOrZero(d.ByHolesPlayed(18)), MaxOrZero(d.ByLength(18))),
            Ruled("Avg9HoleScore", ValueKind.Average, s.ScoringAverage9.Value ?? 0, length,
                Avg(d.ByHolesPlayed(9).Select(Score)) ?? 0, Avg(d.ByLength(9).Select(Score)) ?? 0),
            Ruled("Avg18HoleScore", ValueKind.Average, s.ScoringAverage18.Value ?? 0, length,
                Avg(d.ByHolesPlayed(18).Select(Score)) ?? 0, Avg(d.ByLength(18).Select(Score)) ?? 0),
            Ruled("ParBreakers", ValueKind.Percent, Percent(s.ParBreakers) ?? 0, albatross,
                Pct(d.Holes.Count(h => ToPar(h) is -1 or -2), d.Holes.Count) ?? 0, Pct(d.Holes.Count(h => ToPar(h) <= -1), d.Holes.Count) ?? 0),
            Ruled("BirdieOrBetterAvg", ValueKind.Percent, Percent(s.BirdieOrBetter) ?? 0, albatross,
                Pct(d.Holes.Count(h => ToPar(h) is -1 or -2), d.Holes.Count) ?? 0, Pct(d.Holes.Count(h => ToPar(h) <= -1), d.Holes.Count) ?? 0),
            Plain("Par3Avg", ValueKind.Average, s.Par3Average.Value ?? 0),
            Plain("Par4Avg", ValueKind.Average, s.Par4Average.Value ?? 0),
            Plain("Par5Avg", ValueKind.Average, s.Par5Average.Value ?? 0),
        ];
    }

    private static IReadOnlyList<Spec> DrivingSpecs(Rounds d)
    {
        var o = OffTheTeeStats.Calculate(d.All);
        IntendedRule[] par3 = [IntendedRules.ParThreeTeeShotsIgnored];
        var notPar3 = d.Holes.Count(h => h.Par != 3);
        int All(Func<HoleSummary, bool> flag) => d.Holes.Count(flag);
        int OffPar3(Func<HoleSummary, bool> flag) => d.Holes.Count(h => h.Par != 3 && flag(h));
        double? ScoreFrom(Average toPar, int par) => toPar.Value is { } v ? v + par : 0;
        return
        [
            Plain("TotalHoles", ValueKind.Count, d.Holes.Count, ComputedHere),
            Plain("DrivingHoles", ValueKind.Count, o.DrivingAccuracy.Denominator),
            Ruled("FairwaysHit", ValueKind.Count, o.DrivingAccuracy.Numerator, par3, All(h => h.FairwayHit), OffPar3(h => h.FairwayHit)),
            Ruled("LeftRoughHit", ValueKind.Count, o.LeftTendency.Numerator, par3, All(h => h.TeeShotLeft), OffPar3(h => h.TeeShotLeft)),
            Ruled("RightRoughHit", ValueKind.Count, o.RightTendency.Numerator, par3, All(h => h.TeeShotRight), OffPar3(h => h.TeeShotRight)),
            Plain("TotalPar4s", ValueKind.Count, d.Holes.Count(h => h.Par == 4), ComputedHere),
            Plain("TotalPar5s", ValueKind.Count, d.Holes.Count(h => h.Par == 5), ComputedHere),
            Ruled("FairwayHitAvg", ValueKind.Percent, Percent(o.DrivingAccuracy), par3,
                Pct(All(h => h.FairwayHit), notPar3), Pct(OffPar3(h => h.FairwayHit), notPar3)),
            Ruled("LeftRoughAvg", ValueKind.Percent, Percent(o.LeftTendency), par3,
                Pct(All(h => h.TeeShotLeft), notPar3), Pct(OffPar3(h => h.TeeShotLeft), notPar3)),
            Ruled("RightRoughAvg", ValueKind.Percent, Percent(o.RightTendency), par3,
                Pct(All(h => h.TeeShotRight), notPar3), Pct(OffPar3(h => h.TeeShotRight), notPar3)),
            Plain("Par4FairwayHitAvgScore", ValueKind.Average, ScoreFrom(o.Par4ToParFromFairway, 4)),
            Plain("Par4LeftRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par4ToParFromLeft, 4)),
            Plain("Par4RightRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par4ToParFromRight, 4)),
            Plain("Par5FairwayHitAvgScore", ValueKind.Average, ScoreFrom(o.Par5ToParFromFairway, 5)),
            Plain("Par5LeftRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par5ToParFromLeft, 5)),
            Plain("Par5RightRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par5ToParFromRight, 5)),
        ];
    }

    private static IReadOnlyList<Spec> ApproachSpecs(Rounds d)
    {
        var a = ApproachStats.Calculate(d.All);
        IntendedRule[] par3 = [IntendedRules.ParThreeTeeShotsIgnored];
        IntendedRule[] sand = [IntendedRules.SandSavesGreensideOnly];
        int Tee(Func<HoleSummary, bool> flag, bool offPar3) => d.Holes.Count(h => flag(h) && (!offPar3 || h.Par != 3));
        int GirAfter(Func<HoleSummary, bool> flag, bool offPar3) => d.Holes.Count(h => flag(h) && h.Gir && (!offPar3 || h.Par != 3));
        Spec TeeShot(string column, Ratio ratio, Func<HoleSummary, bool> flag) =>
            Ruled(column, ValueKind.Count, ratio.Denominator, par3, Tee(flag, false), Tee(flag, true));
        Spec GirFrom(string column, Ratio ratio, Func<HoleSummary, bool> flag) =>
            Ruled(column, ValueKind.Count, ratio.Numerator, par3, GirAfter(flag, false), GirAfter(flag, true));
        Spec GirAvg(string column, Ratio ratio, Func<HoleSummary, bool> flag) =>
            Ruled(column, ValueKind.Percent, Percent(ratio) ?? 0, par3,
                Pct(GirAfter(flag, false), Tee(flag, false)) ?? 0, Pct(GirAfter(flag, true), Tee(flag, true)) ?? 0);

        var anySand = d.Holes.Where(h => h.GreensideSandShots + h.FairwaySandShots > 0).ToList();
        var greenside = d.Holes.Where(h => h.GreensideSandShots > 0).ToList();
        return
        [
            Plain("TotalHoles", ValueKind.Count, d.Holes.Count, ComputedHere),
            Plain("GreensInRegulation", ValueKind.Count, a.Gir.Numerator),
            Plain("MissedGIR", ValueKind.Count, a.Gir.Denominator - a.Gir.Numerator),
            Plain("HoleOuts", ValueKind.Count, a.HoleOut.Numerator),
            Plain("HoleOutAvg", ValueKind.Percent, Percent(a.HoleOut) ?? 0),
            Plain("GIRAvg", ValueKind.Percent, Percent(a.Gir) ?? 0),
            GirAvg("GIRFairwayAvg", a.GirFromFairway, h => h.FairwayHit),
            GirAvg("GIRLeftRoughAvg", a.GirFromLeft, h => h.TeeShotLeft),
            GirAvg("GIRRightRoughAvg", a.GirFromRight, h => h.TeeShotRight),
            TeeShot("TeeShotFairway", a.GirFromFairway, h => h.FairwayHit),
            TeeShot("TeeShotLeft", a.GirFromLeft, h => h.TeeShotLeft),
            TeeShot("TeeShotRight", a.GirFromRight, h => h.TeeShotRight),
            GirFrom("GIRFromFairway", a.GirFromFairway, h => h.FairwayHit),
            GirFrom("GIRFromLeft", a.GirFromLeft, h => h.TeeShotLeft),
            GirFrom("GIRFromRight", a.GirFromRight, h => h.TeeShotRight),
            Plain("TotalSandShots", ValueKind.Count, d.Holes.Sum(h => h.GreensideSandShots + h.FairwaySandShots), ComputedHere),
            Ruled("HolesWithSandShots", ValueKind.Count, a.SandSave.Denominator, sand, anySand.Count, greenside.Count),
            Ruled("SandSaves", ValueKind.Count, a.SandSave.Numerator, sand, anySand.Count(h => ToPar(h) <= 0), greenside.Count(h => ToPar(h) <= 0)),
            Ruled("SandSaveAvg", ValueKind.Percent, Percent(a.SandSave) ?? 0, sand,
                Pct(anySand.Count(h => ToPar(h) <= 0), anySand.Count) ?? 0, Pct(greenside.Count(h => ToPar(h) <= 0), greenside.Count) ?? 0),
            Plain("MissedGIRScrambling", ValueKind.Count, a.Scrambling.Numerator),
            Plain("ScramblingAvg", ValueKind.Percent, Percent(a.Scrambling) ?? 0),
        ];
    }

    private static IReadOnlyList<Spec> PuttingSpecs(Rounds d)
    {
        var p = PuttingStats.Calculate(d.All);
        IntendedRule[] length = [IntendedRules.ExactRoundLength];
        IntendedRule[] girOnly = [IntendedRules.BirdieConversionGirOnly];
        var gir = d.Holes.Count(h => h.Gir);
        var birdiesAnywhere = d.Holes.Count(h => ToPar(h) <= -1);
        var girBirdies = d.Holes.Count(h => h.Gir && ToPar(h) <= -1);
        return
        [
            Plain("TotalRounds", ValueKind.Count, d.All.Count, ComputedHere),
            Ruled("9HoleRounds", ValueKind.Count, p.PuttsPerRound9.Count, length, d.ByHolesPlayed(9).Count, d.ByLength(9).Count),
            Ruled("18HoleRounds", ValueKind.Count, p.PuttsPerRound18.Count, length, d.ByHolesPlayed(18).Count, d.ByLength(18).Count),
            Plain("TotalHoles", ValueKind.Count, d.Holes.Count, ComputedHere),
            Plain("TotalGIRHoles", ValueKind.Count, p.PuttsPerGir.Count),
            Plain("TotalPutts", ValueKind.Count, p.PuttsPerHole.Total),
            Plain("TotalPuttsGIR", ValueKind.Count, p.PuttsPerGir.Total),
            Plain("OnePutts", ValueKind.Count, p.OnePutt.Numerator),
            Plain("TwoPutts", ValueKind.Count, d.Holes.Count(h => h.Putts == 2), ComputedHere),
            Plain("ThreePuttsPlus", ValueKind.Count, p.ThreePuttRate.Numerator),
            Ruled("TotalBirdiesPlus", ValueKind.Count, p.BirdieConversion.Numerator, girOnly, birdiesAnywhere, girBirdies),
            Plain("AvgPuttsGIR", ValueKind.Average, p.PuttsPerGir.Value ?? 0),
            Plain("AvgPutts", ValueKind.Average, p.PuttsPerHole.Value ?? 0),
            Plain("OnePuttAvg", ValueKind.Percent, Percent(p.OnePutt) ?? 0),
            Plain("ThreePuttAvg", ValueKind.Percent, Percent(p.ThreePuttRate) ?? 0),
            Ruled("AvgPutts9Holes", ValueKind.Average, p.PuttsPerRound9.Value ?? 0, length,
                Avg(d.ByHolesPlayed(9).Select(Putts)) ?? 0, Avg(d.ByLength(9).Select(Putts)) ?? 0),
            Ruled("AvgPutts18Holes", ValueKind.Average, p.PuttsPerRound18.Value ?? 0, length,
                Avg(d.ByHolesPlayed(18).Select(Putts)) ?? 0, Avg(d.ByLength(18).Select(Putts)) ?? 0),
            Ruled("BirdieConversion", ValueKind.Percent, Percent(p.BirdieConversion) ?? 0, girOnly,
                Pct(birdiesAnywhere, gir) ?? 0, Pct(girBirdies, gir) ?? 0),
        ];
    }

    /// <summary>
    /// Round_SeasonSummary counts joined detail rows: a round contributes one row per hole whose hole is on the round's
    /// own tee (none for a round moved to its holes' tee), bucketed by the round's stored Score whatever its length.
    /// </summary>
    private static IReadOnlyList<Spec> SeasonSpecs(Rounds d)
    {
        var s = SeasonStats.Season(d.All);
        IntendedRule[] defect = [IntendedRules.SeasonSummaryCountsHoleRows];
        int Rows(Func<int, bool> storedScore) =>
            d.All.Where(r => d.StoredScore(r) is { } score && storedScore(score)).Sum(r => d.IsMoved(r) ? 0 : r.Holes.Count);
        int Eighteens(Func<int, bool> score) => d.ByLength(18).Count(r => score(Score(r)));
        return
        [
            Ruled("Rounds", ValueKind.Count, d.All.Count, defect, Rows(_ => true), d.All.Count, ComputedHere),
            Ruled("Rounds9", ValueKind.Count, s.Rounds9, defect, 0, d.ByLength(9).Count),
            Ruled("Rounds18", ValueKind.Count, s.Rounds18, defect, 0, d.ByLength(18).Count),
            Ruled("RoundsUnder80", ValueKind.Count, s.Histogram18[0].Count, defect, Rows(x => x < 80), Eighteens(x => x < 80)),
            Ruled("Rounds8089", ValueKind.Count, s.Histogram18[1].Count, defect, Rows(x => x is >= 80 and < 90), Eighteens(x => x is >= 80 and < 90)),
            Ruled("RoundsOver90", ValueKind.Count, s.Histogram18[2].Count, [IntendedRules.SeasonSummaryCountsHoleRows, IntendedRules.FourBucketHistogram],
                Rows(x => x >= 90), Eighteens(x => x is >= 90 and < 100), "new: the 90-99 bucket"),
        ];
    }

    private static IReadOnlyList<Spec> CareerSpecs(Rounds d)
    {
        var c = CareerStats.ByYear(d.All).SingleOrDefault();
        var legacy = Figures(d.All, d.HolesPlayed, golftrackDriving: true, nullAsZero: false);
        var rule = Figures(d.All, r => r.Holes.Count, golftrackDriving: false, nullAsZero: false);
        return
        [
            .. FigureSpecs(Career(c, d.All.Count), legacy, rule, ruleFor: column => LengthColumns.Contains(column) ? [IntendedRules.ExactRoundLength] : []),
            Plain("TotalCourses", ValueKind.Count, d.All.Select(r => r.CourseId).Distinct().Count(), ComputedHere),
        ];
    }

    // ---- Courses ----

    /// <summary>
    /// Stats_CourseStats groups by course and tee <em>name</em> and only joins holes of the round's own tee. The new rows
    /// are grouped by course and tee name too. Groups holding rounds moved to their holes' tee get the "moved" rule:
    /// golftrack's side is recomputed without them (they have no rows in its join), the new side with them.
    /// </summary>
    private static IEnumerable<ParitySection> CourseSections(LegacyResult legacy, Rounds d)
    {
        if (legacy.Error is not null)
        {
            yield return Section("Courses", legacy, null, [], d.Empty);
            yield break;
        }

        var legacyRows = legacy.Rows
            .Select(row => (Key: (DeterministicIds.For("Course", (int)(Number(row.GetValueOrDefault("Id")) ?? 0)), Name((row.GetValueOrDefault("TeeName") as string) ?? "")), Row: row))
            .ToList();
        var keys = legacyRows.Select(r => r.Key).Concat(d.All.Select(r => (r.CourseId, Name(r.TeeName)))).Distinct().ToList();
        foreach (var key in keys)
        {
            var row = legacyRows.FirstOrDefault(r => r.Key == key).Row;
            var group = d.All.Where(r => (r.CourseId, Name(r.TeeName)) == key).ToList();
            var golftrackGroup = d.All.Where(r => !d.IsMoved(r) && d.LegacyTeeKey(r) == key).ToList();
            var moved = group.Any(d.IsMoved);

            // One synthetic tee id per name, so CareerStats.ByCourseAndTee merges same-named tees as golftrack does.
            var byName = group.Select(r => r with { TeeId = DeterministicIds.Create(DeterministicIds.Namespace, $"parity:{r.CourseId}:{Name(r.TeeName)}") }).ToList();
            var courseRow = CareerStats.ByCourseAndTee(byName).SingleOrDefault();
            var asCareer = courseRow is null
                ? null
                : new CareerRow(0, courseRow.Nine, courseRow.Eighteen, courseRow.Holes, courseRow.ToPar, courseRow.PuttsPerHole,
                    courseRow.Gir, courseRow.DrivingAccuracy, courseRow.Earliest, courseRow.Latest);
            var specs = FigureSpecs(
                Career(asCareer, group.Count, zero: true),
                Figures(golftrackGroup, d.HolesPlayed, golftrackDriving: true, nullAsZero: true),
                Figures(group, r => r.Holes.Count, golftrackDriving: false, nullAsZero: true),
                ruleFor: column => (LengthColumns.Contains(column) ? new[] { IntendedRules.ExactRoundLength } : [])
                    .Concat(moved ? [IntendedRules.RoundMovedToHolesTee] : [])
                    .ToArray());
            var title = (row?.GetValueOrDefault("CourseName") as string) ?? group.FirstOrDefault()?.CourseName ?? "?";
            yield return Section($"Course: {title} / {key.Item2}", legacy, row, specs, group.Count == 0,
                noRowExpected: moved && golftrackGroup.Count == 0);
        }

        static string Name(string teeName) => teeName.Trim();
    }

    // ---- Career and course figures ----

    /// <summary>
    /// The new values of the columns Stats_CareerStats and Stats_CourseStats share, from the domain's row (the round count
    /// is counted here: the domain row has 9 and 18-hole counts only).
    /// </summary>
    private static Dictionary<string, double?> Career(CareerRow? c, int roundCount, bool zero = false)
    {
        double? Z(double? v) => zero ? v ?? 0 : v;
        return new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RoundsPlayed"] = roundCount,
            ["RoundsPlayed9"] = c?.Nine.Rounds ?? 0,
            ["RoundsPlayed18"] = c?.Eighteen.Rounds ?? 0,
            ["HolesPlayed"] = c?.Holes ?? 0,
            ["ScoringAvg9"] = Z(c?.Nine.Average.Value),
            ["ScoringAvg18"] = Z(c?.Eighteen.Average.Value),
            ["RelatedToPar"] = c?.ToPar,
            ["Putts"] = c?.PuttsPerHole.Total,
            ["PuttingAvg"] = c?.PuttsPerHole.Value,
            ["GIR"] = c is null ? null : Percent(c.Gir),
            ["DrivingAccuracy"] = c is null ? null : Percent(c.DrivingAccuracy),
            ["Low9HoleScore"] = Z(c?.Nine.Low),
            ["Low18HoleScore"] = Z(c?.Eighteen.Low),
            ["High9HoleScore"] = Z(c?.Nine.High),
            ["High18HoleScore"] = Z(c?.Eighteen.High),
            ["EarliestRound"] = c?.Earliest.DayNumber,
            ["LatestRound"] = c?.Latest.DayNumber,
        };
    }

    /// <summary>
    /// The same columns recomputed here, independently of the domain: with golftrack's definitions (length from
    /// HolesPlayed, driving over holes that aren't par 3) or with the new rules (length from the holes recorded,
    /// driving over par 4 and 5).
    /// </summary>
    private static Dictionary<string, double?> Figures(
        IReadOnlyList<RoundData> rounds, Func<RoundData, int?> lengthOf, bool golftrackDriving, bool nullAsZero)
    {
        var holes = rounds.SelectMany(r => r.Holes).ToList();
        double? Z(double? v) => nullAsZero ? v ?? 0 : v;
        List<int> Scores(int length) => rounds.Where(r => lengthOf(r) == length).Select(Score).ToList();
        double? Low(int length) => Scores(length) is { Count: > 0 } s ? s.Min() : null;
        double? High(int length) => Scores(length) is { Count: > 0 } s ? s.Max() : null;
        var driving = holes.Where(h => golftrackDriving ? h.Par != 3 : h.Par >= 4).ToList();
        var empty = holes.Count == 0;
        return new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RoundsPlayed"] = rounds.Count,
            ["RoundsPlayed9"] = Scores(9).Count,
            ["RoundsPlayed18"] = Scores(18).Count,
            ["HolesPlayed"] = holes.Count,
            ["ScoringAvg9"] = Z(Avg(Scores(9))),
            ["ScoringAvg18"] = Z(Avg(Scores(18))),
            ["RelatedToPar"] = empty ? null : holes.Sum(ToPar),
            ["Putts"] = empty ? null : holes.Sum(h => h.Putts),
            ["PuttingAvg"] = empty ? null : (double)holes.Sum(h => h.Putts) / holes.Count,
            ["GIR"] = Pct(holes.Count(h => h.Gir), holes.Count),
            ["DrivingAccuracy"] = Pct(driving.Count(h => h.FairwayHit), driving.Count),
            ["Low9HoleScore"] = Z(Low(9)),
            ["Low18HoleScore"] = Z(Low(18)),
            ["High9HoleScore"] = Z(High(9)),
            ["High18HoleScore"] = Z(High(18)),
            ["EarliestRound"] = rounds.Count == 0 ? null : rounds.Min(r => r.PlayedOn).DayNumber,
            ["LatestRound"] = rounds.Count == 0 ? null : rounds.Max(r => r.PlayedOn).DayNumber,
        };
    }

    private static IReadOnlyList<Spec> FigureSpecs(
        Dictionary<string, double?> newValues,
        Dictionary<string, double?> golftrackDefinition,
        Dictionary<string, double?> ruleDefinition,
        Func<string, IntendedRule[]> ruleFor) =>
        newValues
            .Select(kv =>
            {
                var kind = kv.Key switch
                {
                    "ScoringAvg9" or "ScoringAvg18" or "PuttingAvg" => ValueKind.Average,
                    "GIR" or "DrivingAccuracy" => ValueKind.Percent,
                    "RelatedToPar" => ValueKind.ToPar,
                    "EarliestRound" or "LatestRound" => ValueKind.Date,
                    _ => ValueKind.Count,
                };
                var rules = ruleFor(kv.Key);
                var note = kv.Key == "RoundsPlayed" ? ComputedHere : null;
                return rules.Length == 0
                    ? Plain(kv.Key, kind, kv.Value, note)
                    : Ruled(kv.Key, kind, kv.Value, rules, golftrackDefinition[kv.Key], ruleDefinition[kv.Key], note);
            })
            .ToList();

    // ---- Comparison ----

    private static ParitySection Section(
        string title, LegacyResult legacy, IReadOnlyDictionary<string, object?>? row, IReadOnlyList<Spec> specs, bool newIsEmpty,
        string? note = null, bool noRowExpected = false)
    {
        if (legacy.Error is not null)
        {
            return new ParitySection(
                title, legacy.Procedure,
                [new ParityRow("(procedure)", ValueKind.Count, null, null, Verdict.Missing, [], $"golftrack procedure failed: {legacy.Error}")],
                note);
        }

        if (row is null)
        {
            // Every new number is still shown. Without a golftrack row nothing matches; it is intended only when
            // golftrack's definition gives no row (all rounds moved away) and the new value is the rules' value.
            return new ParitySection(
                title, legacy.Procedure,
                specs.Select(s =>
                {
                    var intended = noRowExpected && s.Rules.Count > 0 && Same(s.New, s.RuleDefinition, s.Kind);
                    var verdict = intended ? Verdict.Intended : newIsEmpty ? Verdict.NotCompared : Verdict.Unexplained;
                    return new ParityRow(s.Column, s.Kind, null, s.New, verdict, intended ? s.Rules : [], JoinNotes("golftrack returned no row", s.Note));
                }).ToList(),
                note);
        }

        var rows = new List<ParityRow>();
        foreach (var spec in specs)
        {
            if (!row.TryGetValue(spec.Column, out var raw))
            {
                rows.Add(new ParityRow(spec.Column, spec.Kind, null, spec.New, Verdict.Missing, [], JoinNotes("column missing in golftrack", spec.Note)));
                continue;
            }

            var golftrack = Read(raw, spec.Kind);
            if (golftrack is null && raw is not null)
            {
                rows.Add(new ParityRow(spec.Column, spec.Kind, null, spec.New, Verdict.NotCompared, [],
                    JoinNotes($"unparseable golftrack value: {raw}", spec.Note)));
                continue;
            }

            var verdict = Judge(golftrack, spec.New, spec.Kind, spec.Rules, spec.LegacyDefinition, spec.RuleDefinition);
            rows.Add(new ParityRow(spec.Column, spec.Kind, golftrack, spec.New, verdict, verdict == Verdict.Intended ? spec.Rules : [], spec.Note));
        }

        var compared = specs.Select(s => s.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, raw) in row.Where(c => !compared.Contains(c.Key) && !IdentityColumns.Contains(c.Key)))
        {
            var value = Number(raw);
            var kind = raw is int or long or short or byte ? ValueKind.Count : ValueKind.Average;
            rows.Add(new ParityRow(column, kind, value, null, Verdict.NotCompared, [],
                value is null && raw is not null ? $"no counterpart; golftrack: {raw}" : "no counterpart"));
        }

        return new ParitySection(title, legacy.Procedure, rows, note);
    }

    private static string? JoinNotes(string first, string? second) => second is null ? first : $"{first}; {second}";

    private static bool Same(double? a, double? b, ValueKind kind) =>
        a is null || b is null
            ? a is null && b is null
            : Math.Abs(a.Value - b.Value) <= (kind is ValueKind.Average or ValueKind.Percent ? Tolerance + Exact : Exact);

    private static double? Read(object? raw, ValueKind kind) => kind switch
    {
        _ when raw is null => null,
        ValueKind.Date when raw is DateTime dt => DateOnly.FromDateTime(dt).DayNumber,
        ValueKind.ToPar when raw is string text => text.Trim() == "E" ? 0 : Number(text.Trim().TrimStart('+')),
        ValueKind.Percent => Number(raw) * 100,
        _ => Number(raw),
    };

    private static double? Number(object? raw) => raw switch
    {
        null => null,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null,
        bool b => b ? 1 : 0,
        DateTime => null,
        IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static int ToPar(HoleSummary h) => h.Score - h.Par;

    private static int Score(RoundData r) => r.Holes.Sum(h => h.Score);

    private static int Putts(RoundData r) => r.Holes.Sum(h => h.Putts);

    private static double? Percent(Ratio ratio) => ratio.Value * 100;

    private static double? Pct(int numerator, int denominator) => denominator == 0 ? null : 100.0 * numerator / denominator;

    private static double? Avg(IEnumerable<int> values) => values.ToList() is { Count: > 0 } list ? list.Average() : null;

    private static int MinOrZero(IEnumerable<RoundData> rounds) => rounds.Select(Score).DefaultIfEmpty().Min();

    private static int MaxOrZero(IEnumerable<RoundData> rounds) => rounds.Select(Score).DefaultIfEmpty().Max();

    private static Spec Plain(string column, ValueKind kind, double? newValue, string? note = null) =>
        new(column, kind, newValue, [], null, null, note);

    private static Spec Ruled(
        string column, ValueKind kind, double? newValue, IntendedRule[] rules, double? legacyDefinition, double? ruleDefinition, string? note = null) =>
        new(column, kind, newValue, rules, legacyDefinition, ruleDefinition, note);

    /// <param name="LegacyDefinition">The column computed here on the imported data with golftrack's definition.</param>
    /// <param name="RuleDefinition">The column computed here, independently of the domain, with the rules' definition.</param>
    private sealed record Spec(
        string Column, ValueKind Kind, double? New, IReadOnlyList<IntendedRule> Rules, double? LegacyDefinition, double? RuleDefinition, string? Note);

    private sealed class Rounds(IReadOnlyList<RoundData> rounds, ParityLegacyRounds legacy)
    {
        public IReadOnlyList<RoundData> All { get; } = rounds;

        public IReadOnlyList<HoleSummary> Holes { get; } = rounds.SelectMany(r => r.Holes).ToList();

        public bool Empty => Holes.Count == 0;

        public int? HolesPlayed(RoundData r) => legacy.Rounds.TryGetValue(r.Id, out var l) ? l.HolesPlayed : null;

        public int? StoredScore(RoundData r) => legacy.Rounds.TryGetValue(r.Id, out var l) ? l.Score : null;

        /// <summary>golftrack's 9/18 classification: the round's HolesPlayed field (scores still from the imported holes).</summary>
        public IReadOnlyList<RoundData> ByHolesPlayed(int holesPlayed) => All.Where(r => HolesPlayed(r) == holesPlayed).ToList();

        /// <summary>The new classification, recomputed here: the number of holes recorded.</summary>
        public IReadOnlyList<RoundData> ByLength(int holes) => All.Where(r => r.Holes.Count == holes).ToList();

        /// <summary>Imported on another tee than the golftrack round's own (moved to the tee of its holes).</summary>
        public bool IsMoved(RoundData r) =>
            legacy.Rounds.TryGetValue(r.Id, out var l) && DeterministicIds.For("Tee", l.TeeId) != r.TeeId;

        /// <summary>(course, tee name) of the golftrack round's own tee.</summary>
        public (Guid, string)? LegacyTeeKey(RoundData r) =>
            legacy.Rounds.TryGetValue(r.Id, out var l) && legacy.Tees.TryGetValue(l.TeeId, out var t)
                ? (DeterministicIds.For("Course", t.CourseId), t.Name.Trim())
                : null;
    }
}
