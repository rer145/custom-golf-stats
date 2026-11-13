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

/// <summary>
/// Compares golftrack's numbers with the new domain stats over the imported rounds of one year, column by column.
/// Verdicts:
/// <list type="bullet">
/// <item><b>Match</b>: equal. Counts, to-par and dates exactly; averages and percentages (as 0-100) within ±0.05, which
/// only absorbs golftrack's rounding to 2 decimals (averages) or 4 decimals of a fraction (percentages).</item>
/// <item><b>Intended</b>: different, the column has a rule, and golftrack's value equals the value golftrack's own
/// definition gives on the imported data (computed here). So a rule never excuses a data difference.</item>
/// <item><b>Unexplained</b>: any other difference, including a missing golftrack row.</item>
/// <item><b>Not compared</b>: golftrack columns without a counterpart, missing columns and failed procedures, all listed.</item>
/// </list>
/// Pure: no I/O.
/// </summary>
public static class ParityComparer
{
    private const double Rounding = 0.05;
    private const double Exact = 1e-9;

    private static readonly HashSet<string> IdentityColumns = new(StringComparer.OrdinalIgnoreCase) { "Year", "Id", "CourseName", "TeeName" };

    public static IReadOnlyList<ParitySection> Compare(
        int year, IReadOnlyList<RoundData> rounds, IReadOnlyDictionary<Guid, LegacyRound> legacyRounds, LegacyYearResults legacy)
    {
        var data = new YearData(rounds, legacyRounds);
        var sections = new List<ParitySection>
        {
            Section("Scoring", legacy.Scoring, legacy.Scoring.Rows.FirstOrDefault(), ScoringSpecs(data), data.Empty),
            Section("Off the tee", legacy.Driving, legacy.Driving.Rows.FirstOrDefault(), DrivingSpecs(data), data.Empty),
            Section("Approach", legacy.Approach, legacy.Approach.Rows.FirstOrDefault(), ApproachSpecs(data), data.Empty),
            Section("Putting", legacy.Putting, legacy.Putting.Rows.FirstOrDefault(), PuttingSpecs(data), data.Empty),
            Section("Season", legacy.Season, legacy.Season.Rows.FirstOrDefault(), SeasonSpecs(data), data.Empty,
                "golftrack's Round_SeasonSummary counts round-detail rows (holes), not rounds, over rounds of every length, " +
                "and always returns 0 for Rounds9 and Rounds18. No phase 1 rule covers that, so those rows show as unexplained."),
            Section(
                "Career (this year)",
                legacy.Career,
                legacy.Career.Rows.FirstOrDefault(r => Number(r.GetValueOrDefault("Year")) == year),
                CareerSpecs(data, CareerStats.ByYear(rounds).SingleOrDefault(), averagesNullAsZero: false),
                data.Empty),
        };
        sections.AddRange(CourseSections(legacy.Courses, rounds, legacyRounds));
        return sections;
    }

    // ---- Sections ----

    private static IReadOnlyList<Spec> ScoringSpecs(YearData d)
    {
        var s = ScoringStats.Calculate(d.Rounds);
        return
        [
            new("TotalHoles", ValueKind.Count, d.Holes.Count),
            new("TotalPar3Holes", ValueKind.Count, s.Par3Average.Count),
            new("TotalPar4Holes", ValueKind.Count, s.Par4Average.Count),
            new("TotalPar5Holes", ValueKind.Count, s.Par5Average.Count),
            new("Total9HoleRounds", ValueKind.Count, s.ScoringAverage9.Count, IntendedRules.ExactRoundLength, d.ByHolesPlayed(9).Count),
            new("Total18HoleRounds", ValueKind.Count, s.ScoringAverage18.Count, IntendedRules.ExactRoundLength, d.ByHolesPlayed(18).Count),
            new("Birdies", ValueKind.Count, s.Birdies),
            new("Eagles", ValueKind.Count, s.Eagles, IntendedRules.EaglesIncludeAlbatrosses, d.Holes.Count(h => h.ToPar == -2)),
            new("BirdiesAndEagles", ValueKind.Count, s.Birdies + s.Eagles, IntendedRules.EaglesIncludeAlbatrosses,
                d.Holes.Count(h => h.ToPar is -1 or -2)),
            new("TotalPar3Score", ValueKind.Count, s.Par3Average.Total),
            new("TotalPar4Score", ValueKind.Count, s.Par4Average.Total),
            new("TotalPar5Score", ValueKind.Count, s.Par5Average.Total),
            new("Low9HoleRound", ValueKind.Count, s.Lowest9 ?? 0, IntendedRules.ExactRoundLength, d.ByHolesPlayed(9).Select(r => r.Score).DefaultIfEmpty().Min()),
            new("High9HoleRound", ValueKind.Count, s.Highest9 ?? 0, IntendedRules.ExactRoundLength, d.ByHolesPlayed(9).Select(r => r.Score).DefaultIfEmpty().Max()),
            new("Low18HoleRound", ValueKind.Count, s.Lowest18 ?? 0, IntendedRules.ExactRoundLength, d.ByHolesPlayed(18).Select(r => r.Score).DefaultIfEmpty().Min()),
            new("High18HoleRound", ValueKind.Count, s.Highest18 ?? 0, IntendedRules.ExactRoundLength, d.ByHolesPlayed(18).Select(r => r.Score).DefaultIfEmpty().Max()),
            new("Avg9HoleScore", ValueKind.Average, s.ScoringAverage9.Value ?? 0, IntendedRules.ExactRoundLength, AverageOrZero(d.ByHolesPlayed(9).Select(r => r.Score))),
            new("Avg18HoleScore", ValueKind.Average, s.ScoringAverage18.Value ?? 0, IntendedRules.ExactRoundLength, AverageOrZero(d.ByHolesPlayed(18).Select(r => r.Score))),
            new("ParBreakers", ValueKind.Percent, Percent(s.ParBreakers) ?? 0, IntendedRules.EaglesIncludeAlbatrosses,
                Percent(new Ratio(d.Holes.Count(h => h.ToPar is -1 or -2), d.Holes.Count)) ?? 0),
            new("BirdieOrBetterAvg", ValueKind.Percent, Percent(s.BirdieOrBetter) ?? 0, IntendedRules.EaglesIncludeAlbatrosses,
                Percent(new Ratio(d.Holes.Count(h => h.ToPar is -1 or -2), d.Holes.Count)) ?? 0),
            new("Par3Avg", ValueKind.Average, s.Par3Average.Value ?? 0),
            new("Par4Avg", ValueKind.Average, s.Par4Average.Value ?? 0),
            new("Par5Avg", ValueKind.Average, s.Par5Average.Value ?? 0),
        ];
    }

    private static IReadOnlyList<Spec> DrivingSpecs(YearData d)
    {
        var o = OffTheTeeStats.Calculate(d.Rounds);
        var notPar3 = d.Holes.Count(h => h.Par != 3);
        double? ScoreFrom(Average toPar, int par) => toPar.Value is { } v ? v + par : 0;
        return
        [
            new("TotalHoles", ValueKind.Count, d.Holes.Count),
            new("DrivingHoles", ValueKind.Count, o.DrivingAccuracy.Denominator),
            new("FairwaysHit", ValueKind.Count, o.DrivingAccuracy.Numerator, IntendedRules.ParThreeTeeShotsIgnored, d.Holes.Count(h => h.FairwayHit)),
            new("LeftRoughHit", ValueKind.Count, o.LeftTendency.Numerator, IntendedRules.ParThreeTeeShotsIgnored, d.Holes.Count(h => h.TeeShotLeft)),
            new("RightRoughHit", ValueKind.Count, o.RightTendency.Numerator, IntendedRules.ParThreeTeeShotsIgnored, d.Holes.Count(h => h.TeeShotRight)),
            new("TotalPar4s", ValueKind.Count, d.Holes.Count(h => h.Par == 4)),
            new("TotalPar5s", ValueKind.Count, d.Holes.Count(h => h.Par == 5)),
            new("FairwayHitAvg", ValueKind.Percent, Percent(o.DrivingAccuracy), IntendedRules.ParThreeTeeShotsIgnored,
                Percent(new Ratio(d.Holes.Count(h => h.FairwayHit), notPar3))),
            new("LeftRoughAvg", ValueKind.Percent, Percent(o.LeftTendency), IntendedRules.ParThreeTeeShotsIgnored,
                Percent(new Ratio(d.Holes.Count(h => h.TeeShotLeft), notPar3))),
            new("RightRoughAvg", ValueKind.Percent, Percent(o.RightTendency), IntendedRules.ParThreeTeeShotsIgnored,
                Percent(new Ratio(d.Holes.Count(h => h.TeeShotRight), notPar3))),
            new("Par4FairwayHitAvgScore", ValueKind.Average, ScoreFrom(o.Par4ToParFromFairway, 4)),
            new("Par4LeftRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par4ToParFromLeft, 4)),
            new("Par4RightRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par4ToParFromRight, 4)),
            new("Par5FairwayHitAvgScore", ValueKind.Average, ScoreFrom(o.Par5ToParFromFairway, 5)),
            new("Par5LeftRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par5ToParFromLeft, 5)),
            new("Par5RightRoughAvgScore", ValueKind.Average, ScoreFrom(o.Par5ToParFromRight, 5)),
        ];
    }

    private static IReadOnlyList<Spec> ApproachSpecs(YearData d)
    {
        var a = ApproachStats.Calculate(d.Rounds);
        Ratio GirAfter(Func<HoleSummary, bool> teeShot) =>
            new(d.Holes.Count(h => teeShot(h) && h.Gir), d.Holes.Count(teeShot));
        var anySand = d.Holes.Where(h => h.GreensideSandShots + h.FairwaySandShots > 0).ToList();
        var legacySandSave = new Ratio(anySand.Count(h => h.ToPar <= 0), anySand.Count);
        return
        [
            new("TotalHoles", ValueKind.Count, d.Holes.Count),
            new("GreensInRegulation", ValueKind.Count, a.Gir.Numerator),
            new("MissedGIR", ValueKind.Count, a.Gir.Denominator - a.Gir.Numerator),
            new("HoleOuts", ValueKind.Count, a.HoleOut.Numerator),
            new("HoleOutAvg", ValueKind.Percent, Percent(a.HoleOut) ?? 0),
            new("GIRAvg", ValueKind.Percent, Percent(a.Gir) ?? 0),
            new("GIRFairwayAvg", ValueKind.Percent, Percent(a.GirFromFairway) ?? 0, IntendedRules.ParThreeTeeShotsIgnored, Percent(GirAfter(h => h.FairwayHit)) ?? 0),
            new("GIRLeftRoughAvg", ValueKind.Percent, Percent(a.GirFromLeft) ?? 0, IntendedRules.ParThreeTeeShotsIgnored, Percent(GirAfter(h => h.TeeShotLeft)) ?? 0),
            new("GIRRightRoughAvg", ValueKind.Percent, Percent(a.GirFromRight) ?? 0, IntendedRules.ParThreeTeeShotsIgnored, Percent(GirAfter(h => h.TeeShotRight)) ?? 0),
            new("TeeShotFairway", ValueKind.Count, a.GirFromFairway.Denominator, IntendedRules.ParThreeTeeShotsIgnored, GirAfter(h => h.FairwayHit).Denominator),
            new("TeeShotLeft", ValueKind.Count, a.GirFromLeft.Denominator, IntendedRules.ParThreeTeeShotsIgnored, GirAfter(h => h.TeeShotLeft).Denominator),
            new("TeeShotRight", ValueKind.Count, a.GirFromRight.Denominator, IntendedRules.ParThreeTeeShotsIgnored, GirAfter(h => h.TeeShotRight).Denominator),
            new("GIRFromFairway", ValueKind.Count, a.GirFromFairway.Numerator, IntendedRules.ParThreeTeeShotsIgnored, GirAfter(h => h.FairwayHit).Numerator),
            new("GIRFromLeft", ValueKind.Count, a.GirFromLeft.Numerator, IntendedRules.ParThreeTeeShotsIgnored, GirAfter(h => h.TeeShotLeft).Numerator),
            new("GIRFromRight", ValueKind.Count, a.GirFromRight.Numerator, IntendedRules.ParThreeTeeShotsIgnored, GirAfter(h => h.TeeShotRight).Numerator),
            new("TotalSandShots", ValueKind.Count, d.Holes.Sum(h => h.GreensideSandShots + h.FairwaySandShots)),
            new("HolesWithSandShots", ValueKind.Count, a.SandSave.Denominator, IntendedRules.SandSavesGreensideOnly, legacySandSave.Denominator),
            new("SandSaves", ValueKind.Count, a.SandSave.Numerator, IntendedRules.SandSavesGreensideOnly, legacySandSave.Numerator),
            new("SandSaveAvg", ValueKind.Percent, Percent(a.SandSave) ?? 0, IntendedRules.SandSavesGreensideOnly, Percent(legacySandSave) ?? 0),
            new("MissedGIRScrambling", ValueKind.Count, a.Scrambling.Numerator),
            new("ScramblingAvg", ValueKind.Percent, Percent(a.Scrambling) ?? 0),
        ];
    }

    private static IReadOnlyList<Spec> PuttingSpecs(YearData d)
    {
        var p = PuttingStats.Calculate(d.Rounds);
        var birdiesAnywhere = new Ratio(d.Holes.Count(h => h.ToPar <= -1), p.PuttsPerGir.Count);
        double PuttsPerRound(int holesPlayed) => AverageOrZero(d.ByHolesPlayed(holesPlayed).Select(r => r.Putts));
        return
        [
            new("TotalRounds", ValueKind.Count, d.Rounds.Count),
            new("9HoleRounds", ValueKind.Count, p.PuttsPerRound9.Count, IntendedRules.ExactRoundLength, d.ByHolesPlayed(9).Count),
            new("18HoleRounds", ValueKind.Count, p.PuttsPerRound18.Count, IntendedRules.ExactRoundLength, d.ByHolesPlayed(18).Count),
            new("TotalHoles", ValueKind.Count, d.Holes.Count),
            new("TotalGIRHoles", ValueKind.Count, p.PuttsPerGir.Count),
            new("TotalPutts", ValueKind.Count, p.PuttsPerHole.Total),
            new("TotalPuttsGIR", ValueKind.Count, p.PuttsPerGir.Total),
            new("OnePutts", ValueKind.Count, p.OnePutt.Numerator),
            new("TwoPutts", ValueKind.Count, d.Holes.Count(h => h.Putts == 2)),
            new("ThreePuttsPlus", ValueKind.Count, p.ThreePuttRate.Numerator),
            new("TotalBirdiesPlus", ValueKind.Count, p.BirdieConversion.Numerator, IntendedRules.BirdieConversionGirOnly, birdiesAnywhere.Numerator),
            new("AvgPuttsGIR", ValueKind.Average, p.PuttsPerGir.Value ?? 0),
            new("AvgPutts", ValueKind.Average, p.PuttsPerHole.Value ?? 0),
            new("OnePuttAvg", ValueKind.Percent, Percent(p.OnePutt) ?? 0),
            new("ThreePuttAvg", ValueKind.Percent, Percent(p.ThreePuttRate) ?? 0),
            new("AvgPutts9Holes", ValueKind.Average, p.PuttsPerRound9.Value ?? 0, IntendedRules.ExactRoundLength, PuttsPerRound(9)),
            new("AvgPutts18Holes", ValueKind.Average, p.PuttsPerRound18.Value ?? 0, IntendedRules.ExactRoundLength, PuttsPerRound(18)),
            new("BirdieConversion", ValueKind.Percent, Percent(p.BirdieConversion) ?? 0, IntendedRules.BirdieConversionGirOnly, Percent(birdiesAnywhere) ?? 0),
        ];
    }

    private static IReadOnlyList<Spec> SeasonSpecs(YearData d)
    {
        var s = SeasonStats.Season(d.Rounds);
        return
        [
            new("Rounds", ValueKind.Count, d.Rounds.Count),
            new("Rounds9", ValueKind.Count, s.Rounds9),
            new("Rounds18", ValueKind.Count, s.Rounds18),
            new("RoundsUnder80", ValueKind.Count, s.Histogram18[0].Count),
            new("Rounds8089", ValueKind.Count, s.Histogram18[1].Count),
            new("RoundsOver90", ValueKind.Count, s.Histogram18[2].Count, IntendedRules.FourBucketHistogram,
                s.Histogram18[2].Count + s.Histogram18[3].Count, "new: 90-99 bucket"),
        ];
    }

    /// <summary>Stats_CareerStats (one year) and Stats_CourseStats (one course and tee name) share their columns.</summary>
    private static IReadOnlyList<Spec> CareerSpecs(YearData d, CareerRow? c, bool averagesNullAsZero)
    {
        double? OrZero(double? value) => averagesNullAsZero ? value ?? 0 : value;
        double? Average(IEnumerable<int> values) => averagesNullAsZero ? AverageOrZero(values) : values.ToList() is { Count: > 0 } l ? l.Average() : null;
        double? Low(int holesPlayed) => OrZero(d.ByHolesPlayed(holesPlayed).Select(r => (int?)r.Score).Min());
        double? High(int holesPlayed) => OrZero(d.ByHolesPlayed(holesPlayed).Select(r => (int?)r.Score).Max());
        return
        [
            new("RoundsPlayed", ValueKind.Count, d.Rounds.Count),
            new("RoundsPlayed9", ValueKind.Count, c?.Nine.Rounds ?? 0, IntendedRules.ExactRoundLength, d.ByHolesPlayed(9).Count),
            new("RoundsPlayed18", ValueKind.Count, c?.Eighteen.Rounds ?? 0, IntendedRules.ExactRoundLength, d.ByHolesPlayed(18).Count),
            new("HolesPlayed", ValueKind.Count, c?.Holes ?? 0),
            new("ScoringAvg9", ValueKind.Average, OrZero(c?.Nine.Average.Value), IntendedRules.ExactRoundLength, Average(d.ByHolesPlayed(9).Select(r => r.Score))),
            new("ScoringAvg18", ValueKind.Average, OrZero(c?.Eighteen.Average.Value), IntendedRules.ExactRoundLength, Average(d.ByHolesPlayed(18).Select(r => r.Score))),
            new("RelatedToPar", ValueKind.ToPar, c?.ToPar),
            new("Putts", ValueKind.Count, c?.PuttsPerHole.Total),
            new("PuttingAvg", ValueKind.Average, c?.PuttsPerHole.Value),
            new("GIR", ValueKind.Percent, c is null ? null : Percent(c.Gir)),
            new("DrivingAccuracy", ValueKind.Percent, c is null ? null : Percent(c.DrivingAccuracy)),
            new("Low9HoleScore", ValueKind.Count, OrZero(c?.Nine.Low), IntendedRules.ExactRoundLength, Low(9)),
            new("Low18HoleScore", ValueKind.Count, OrZero(c?.Eighteen.Low), IntendedRules.ExactRoundLength, Low(18)),
            new("High9HoleScore", ValueKind.Count, OrZero(c?.Nine.High), IntendedRules.ExactRoundLength, High(9)),
            new("High18HoleScore", ValueKind.Count, OrZero(c?.Eighteen.High), IntendedRules.ExactRoundLength, High(18)),
            new("EarliestRound", ValueKind.Date, c?.Earliest.DayNumber),
            new("LatestRound", ValueKind.Date, c?.Latest.DayNumber),
            new("TotalCourses", ValueKind.Count, d.Rounds.Select(r => r.CourseId).Distinct().Count()),
        ];
    }

    /// <summary>
    /// Stats_CourseStats groups by course and tee <em>name</em>; the new rows are grouped the same way (by course and
    /// tee name) so that tees sharing a name are compared together. Rows on one side only are unexplained.
    /// </summary>
    private static IEnumerable<ParitySection> CourseSections(
        LegacyResult legacy, IReadOnlyList<RoundData> rounds, IReadOnlyDictionary<Guid, LegacyRound> legacyRounds)
    {
        if (legacy.Error is not null)
        {
            yield return Section("Courses", legacy, null, [], newIsEmpty: rounds.Count == 0);
            yield break;
        }

        var groups = rounds.GroupBy(r => (r.CourseId, TeeName: r.TeeName.Trim())).ToDictionary(g => g.Key, g => g.ToList());
        var seen = new HashSet<(Guid, string)>();
        foreach (var row in legacy.Rows)
        {
            var courseId = DeterministicIds.For("Course", (int)(Number(row.GetValueOrDefault("Id")) ?? 0));
            var teeName = (row.GetValueOrDefault("TeeName") as string ?? "").Trim();
            var key = (courseId, teeName);
            seen.Add(key);
            var group = groups.GetValueOrDefault(key) ?? [];
            yield return CourseSection($"{row.GetValueOrDefault("CourseName")} / {teeName}", legacy, row, group, legacyRounds);
        }

        foreach (var (key, group) in groups.Where(g => !seen.Contains(g.Key)))
        {
            yield return CourseSection($"{group[0].CourseName} / {key.TeeName}", legacy, null, group, legacyRounds);
        }
    }

    private static ParitySection CourseSection(
        string title, LegacyResult legacy, IReadOnlyDictionary<string, object?>? row, List<RoundData> group,
        IReadOnlyDictionary<Guid, LegacyRound> legacyRounds)
    {
        // One synthetic tee id per name, so CareerStats.ByCourseAndTee merges same-named tees as golftrack does.
        var byName = group.Select(r => r with { TeeId = DeterministicIds.Create(DeterministicIds.Namespace, $"parity:{r.CourseId}:{r.TeeName.Trim()}") }).ToList();
        var courseRow = CareerStats.ByCourseAndTee(byName).SingleOrDefault();
        var data = new YearData(group, legacyRounds);
        var specs = CareerSpecs(data, courseRow is null ? null : AsCareerRow(courseRow), averagesNullAsZero: true)
            .Where(s => s.Column != "TotalCourses")
            .ToList();
        return Section($"Course: {title}", legacy, row, specs, data.Empty);
    }

    private static CareerRow AsCareerRow(CourseRow c) =>
        new(0, c.Nine, c.Eighteen, c.Holes, c.ToPar, c.PuttsPerHole, c.Gir, c.DrivingAccuracy, c.Earliest, c.Latest);

    // ---- Comparison ----

    private static ParitySection Section(
        string title, LegacyResult legacy, IReadOnlyDictionary<string, object?>? row, IReadOnlyList<Spec> specs, bool newIsEmpty,
        string? note = null)
    {
        if (legacy.Error is not null)
        {
            return new ParitySection(
                title, legacy.Procedure,
                [new ParityRow("(procedure)", ValueKind.Count, null, null, Verdict.NotCompared, Note: $"golftrack procedure failed: {legacy.Error}")],
                note);
        }

        if (row is null)
        {
            // Every new number is still shown; without a golftrack row none of them can match.
            var verdict = newIsEmpty ? Verdict.NotCompared : Verdict.Unexplained;
            return new ParitySection(
                title, legacy.Procedure,
                specs.Select(s => new ParityRow(s.Column, s.Kind, null, s.New, verdict, Note: "golftrack returned no row")).ToList(),
                note);
        }

        var rows = new List<ParityRow>();
        foreach (var spec in specs)
        {
            if (!row.TryGetValue(spec.Column, out var raw))
            {
                rows.Add(new ParityRow(spec.Column, spec.Kind, null, spec.New, Verdict.NotCompared, Note: "column missing in golftrack"));
                continue;
            }

            var golftrack = Read(raw, spec.Kind);
            var verdict = Same(golftrack, spec.New, spec.Kind) ? Verdict.Match
                : spec.Rule is not null && Same(golftrack, spec.LegacyDefinition, spec.Kind) ? Verdict.Intended
                : Verdict.Unexplained;
            rows.Add(new ParityRow(spec.Column, spec.Kind, golftrack, spec.New, verdict, verdict == Verdict.Intended ? spec.Rule : null, spec.Note));
        }

        var compared = specs.Select(s => s.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, raw) in row.Where(c => !compared.Contains(c.Key) && !IdentityColumns.Contains(c.Key)))
        {
            var value = Number(raw);
            var kind = raw is int or long or short or byte ? ValueKind.Count : ValueKind.Average;
            rows.Add(new ParityRow(column, kind, value, null, Verdict.NotCompared,
                Note: value is null && raw is not null ? $"no counterpart; golftrack: {raw}" : "no counterpart"));
        }

        return new ParitySection(title, legacy.Procedure, rows, note);
    }

    private static bool Same(double? a, double? b, ValueKind kind) =>
        a is null || b is null
            ? a is null && b is null
            : Math.Abs(a.Value - b.Value) <= (kind is ValueKind.Average or ValueKind.Percent ? Rounding + Exact : Exact);

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

    private static double? Percent(Ratio ratio) => ratio.Value * 100;

    private static double AverageOrZero(IEnumerable<int> values) => values.ToList() is { Count: > 0 } list ? list.Average() : 0;

    /// <param name="LegacyDefinition">The column computed on the imported data with golftrack's definition (only with a rule).</param>
    private sealed record Spec(
        string Column, ValueKind Kind, double? New, IntendedRule? Rule = null, double? LegacyDefinition = null, string? Note = null);

    private sealed class YearData(IReadOnlyList<RoundData> rounds, IReadOnlyDictionary<Guid, LegacyRound> legacyRounds)
    {
        public IReadOnlyList<RoundData> Rounds { get; } = rounds;

        public IReadOnlyList<HoleSummary> Holes { get; } = rounds.SelectMany(r => r.Holes).ToList();

        public bool Empty => Holes.Count == 0;

        /// <summary>golftrack's 9/18 classification: the round's HolesPlayed field (scores still from the imported holes).</summary>
        public IReadOnlyList<RoundData> ByHolesPlayed(int holesPlayed) =>
            Rounds.Where(r => legacyRounds.TryGetValue(r.Id, out var l) && l.HolesPlayed == holesPlayed).ToList();
    }
}
