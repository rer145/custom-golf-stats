using System.Globalization;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Domain;
using CustomGolfStats.Migration.Report;
using CustomGolfStats.Migration.Source;

namespace CustomGolfStats.Migration.Mapping;

/// <summary>
/// The entities to write for one golfer (not saved; every id deterministic) and what the mapping noticed. Courses carry
/// their tees, one tee version each, and its holes; rounds carry their hole results and point at a tee version by id.
/// </summary>
public sealed record MappedImport(
    IReadOnlyList<Course> Courses,
    IReadOnlyList<Round> Rounds,
    IReadOnlyList<HandicapEntry> HandicapEntries,
    IReadOnlyList<MigrationFinding> Findings);

/// <summary>Turns golftrack rows into Custom Golf Stats entities. Pure: no I/O, same input gives the same output.</summary>
public static class GolftrackMapper
{
    private const int MaxNotes = 4000;
    private const decimal MaxDistanceMiles = 9999.99m;

    public static MappedImport Map(LegacyData data, Guid accountId, DateTimeOffset now)
    {
        var findings = new List<MigrationFinding>();
        var coursesById = data.Courses.ToDictionary(c => c.Id);
        var mappedCourses = new Dictionary<int, Course>();
        var tees = new Dictionary<int, MappedTee>();

        foreach (var legacyTee in data.Tees.OrderBy(t => t.Id))
        {
            var holes = data.Holes.Where(h => h.TeeId == legacyTee.Id).ToList();
            if (!coursesById.TryGetValue(legacyTee.CourseId, out var legacyCourse))
            {
                findings.Add(new TeeSkipped(legacyTee.Id, $"course {legacyTee.CourseId}", legacyTee.Name, "its course is missing"));
                continue;
            }

            var version = MapTeeVersion(legacyCourse, legacyTee, holes, accountId, now, findings);
            if (version is null)
            {
                continue;
            }

            if (!mappedCourses.TryGetValue(legacyCourse.Id, out var course))
            {
                course = MapCourse(legacyCourse, accountId, now);
                mappedCourses.Add(legacyCourse.Id, course);
            }

            var tee = new Tee
            {
                Id = DeterministicIds.For("Tee", legacyTee.Id),
                CourseId = course.Id,
                Name = Truncate(legacyTee.Name.Trim(), 100) is { Length: > 0 } name ? name : "Tee",
                Gender = legacyTee.Gender?.Trim().ToUpperInvariant() switch
                {
                    "M" => Gender.Male,
                    "F" => Gender.Female,
                    _ => Gender.Unspecified,
                },
            };
            version.TeeId = tee.Id;
            tee.Versions.Add(version);
            course.Tees.Add(tee);
            tees.Add(legacyTee.Id, new MappedTee(version, holes.ToDictionary(h => h.Id)));
        }

        var detailsByRound = data.RoundDetails.GroupBy(d => d.RoundId).ToDictionary(g => g.Key, g => g.OrderBy(d => d.Id).ToList());
        var rounds = new List<(LegacyRound Legacy, Round Round)>();
        foreach (var legacyRound in data.Rounds.OrderBy(r => r.Id))
        {
            var round = MapRound(data, legacyRound, tees, detailsByRound.GetValueOrDefault(legacyRound.Id) ?? [], accountId, now, findings);
            if (round is not null)
            {
                rounds.Add((legacyRound, round));
            }
        }

        findings.AddRange(DuplicateRounds(rounds));

        var handicaps = data.Handicaps
            .OrderBy(h => h.Id)
            .Select(h => new HandicapEntry
            {
                Id = DeterministicIds.For("HandicapHistory", h.Id),
                AccountId = accountId,
                EffectiveOn = DateOnly.FromDateTime(h.HandicapDate),
                Index = h.HandicapIndex,
            })
            .ToList();

        return new MappedImport(mappedCourses.Values.ToList(), rounds.Select(r => r.Round).ToList(), handicaps, findings);
    }

    private static Course MapCourse(LegacyCourse legacy, Guid accountId, DateTimeOffset now)
    {
        var hasCoordinates = legacy.Latitude is >= -90 and <= 90 && legacy.Longitude is >= -180 and <= 180;
        var address = string.Join(", ", new[] { legacy.Address, legacy.Address2 }.Select(CourseRules.Clean).OfType<string>());
        return new Course
        {
            Id = DeterministicIds.For("Course", legacy.Id),
            Name = Truncate(CourseRules.Clean(legacy.Name), 200) ?? $"Course {legacy.Id}",
            Address = Truncate(CourseRules.Clean(address), 300),
            City = Truncate(CourseRules.Clean(legacy.City), 100),
            Phone = Truncate(CourseRules.Clean(legacy.PhoneNumber), 50),
            Website = Truncate(CourseRules.Clean(legacy.Website), 300),
            Latitude = hasCoordinates ? (double)legacy.Latitude!.Value : null,
            Longitude = hasCoordinates ? (double)legacy.Longitude!.Value : null,
            Status = CourseStatus.Verified,
            Source = CourseSource.Golftrack,
            ProviderCourseId = legacy.Id.ToString(CultureInfo.InvariantCulture),
            LastVerifiedAt = now,
            CreatedByAccountId = accountId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// The tee's single version, or null (and a finding) when the tee has no holes or more than 18. Round details keep
    /// pointing at their own hole row (by id), so a detail on a duplicate row still gets that row's hole number.
    /// </summary>
    private static TeeVersion? MapTeeVersion(
        LegacyCourse course, LegacyTee tee, List<LegacyHole> holes, Guid accountId, DateTimeOffset now, List<MigrationFinding> findings)
    {
        // One row per hole number: the active one (lowest id among several), else the lowest id. Rows that differ
        // in par, yardage or stroke index are reported; identical copies are not.
        var kept = new List<LegacyHole>();
        foreach (var group in holes.GroupBy(h => h.Number).OrderBy(g => g.Key))
        {
            var rows = group.OrderByDescending(h => h.IsActive).ThenBy(h => h.Id).ToList();
            kept.Add(rows[0]);
            if (rows.Select(h => (h.Par, h.Yardage, h.Handicap)).Distinct().Count() > 1)
            {
                findings.Add(new DuplicateTeeHoleNumber(tee.Id, group.Key, rows[0].Id, group.Select(h => h.Id).Order().ToList()));
            }
        }

        if (kept.Count == 0 || kept.Count > CourseRules.MaxHoles)
        {
            findings.Add(new TeeSkipped(tee.Id, course.Name, tee.Name, kept.Count == 0 ? "no holes" : $"{kept.Count} holes"));
            return null;
        }

        var version = new TeeVersion
        {
            Id = DeterministicIds.For("TeeVersion", tee.Id),
            EffectiveFrom = CourseRules.DefaultEffectiveFrom,
            Rating = Rating(tee, "Rating", tee.CourseRating, 0m, findings, course) ?? 0m,
            Slope = Slope(tee, "Slope", tee.SlopeRating, CourseRules.StandardSlope, findings, course) ?? CourseRules.StandardSlope,
            FrontRating = Rating(tee, "FrontRating", tee.FrontNineCourseRating, null, findings, course),
            FrontSlope = Slope(tee, "FrontSlope", tee.FrontNineSlopeRating, null, findings, course),
            BackRating = Rating(tee, "BackRating", tee.BackNineCourseRating, null, findings, course),
            BackSlope = Slope(tee, "BackSlope", tee.BackNineSlopeRating, null, findings, course),
            Source = CourseSource.Golftrack,
            ChangedByAccountId = accountId,
            ChangedAt = now,
        };

        var strokeIndexes = CourseImport.NormalizeStrokeIndexes(kept.Select(h => h.Handicap).ToList());
        CourseRules.SetHoles(version, kept.Select((h, i) => (h.Number, h.Par, h.Yardage, strokeIndexes[i])).ToList());
        foreach (var hole in version.Holes)
        {
            hole.Id = DeterministicIds.For("Hole", kept.Single(h => h.Number == hole.Number).Id);
            hole.TeeVersionId = version.Id;
        }

        return version;
    }

    /// <summary>
    /// A rating in range, rounded to one decimal; otherwise <paramref name="fallback"/>, reported unless the value was
    /// simply absent and there is no fallback (front and back nine ratings are often not recorded).
    /// </summary>
    private static decimal? Rating(
        LegacyTee tee, string field, double? value, decimal? fallback, List<MigrationFinding> findings, LegacyCourse course)
    {
        if (value is { } v && v >= (double)CourseRules.MinRating && v <= (double)CourseRules.MaxRating)
        {
            return Math.Round((decimal)v, 1);
        }

        if (value is not null || fallback is not null)
        {
            findings.Add(new TeeRatingDefaulted(
                tee.Id, course.Name, tee.Name, field, value?.ToString(CultureInfo.InvariantCulture),
                fallback?.ToString(CultureInfo.InvariantCulture)));
        }

        return fallback;
    }

    /// <inheritdoc cref="Rating"/>
    private static int? Slope(
        LegacyTee tee, string field, int? value, int? fallback, List<MigrationFinding> findings, LegacyCourse course)
    {
        if (value is >= CourseRules.MinSlope and <= CourseRules.MaxSlope)
        {
            return value;
        }

        if (value is not null || fallback is not null)
        {
            findings.Add(new TeeRatingDefaulted(
                tee.Id, course.Name, tee.Name, field, value?.ToString(CultureInfo.InvariantCulture),
                fallback?.ToString(CultureInfo.InvariantCulture)));
        }

        return fallback;
    }

    private static Round? MapRound(
        LegacyData data,
        LegacyRound legacy,
        Dictionary<int, MappedTee> tees,
        List<LegacyRoundDetail> details,
        Guid accountId,
        DateTimeOffset now,
        List<MigrationFinding> findings)
    {
        var playedOn = DateOnly.FromDateTime(legacy.PlayedOn);
        if (!tees.TryGetValue(legacy.TeeId, out var tee))
        {
            findings.Add(new RoundSkipped(legacy.Id, playedOn, $"tee {legacy.TeeId} is missing or has no usable holes"));
            return null;
        }

        if (details.Count == 0)
        {
            findings.Add(new RoundSkipped(legacy.Id, playedOn, "no active hole details"));
            return null;
        }

        var missing = details.Where(d => !tee.HolesById.ContainsKey(d.HoleId)).ToList();
        if (missing.Count > 0)
        {
            findings.Add(new RoundSkipped(
                legacy.Id,
                playedOn,
                "hole " + string.Join(", ", missing.Select(d => d.HoleId.ToString(CultureInfo.InvariantCulture))) +
                $" is missing or not on tee {legacy.TeeId}"));
            return null;
        }

        var round = new Round
        {
            Id = DeterministicIds.For("Round", legacy.Id),
            AccountId = accountId,
            TeeVersionId = tee.Version.Id,
            PlayedOn = playedOn,
            EntryMode = EntryMode.Quick,
            RoundType = MapRoundType(data, legacy, playedOn, findings),
            Notes = string.IsNullOrWhiteSpace(legacy.Notes) ? null : Truncate(legacy.Notes, MaxNotes),
            LegacyWeather = LegacyWeather(data, legacy),
            DurationMinutes = Duration(legacy, playedOn, findings),
            DistanceMiles = Distance(legacy, playedOn, findings),
            ClientUpdatedAt = now,
            ModifiedAt = now,
            TrialCounted = false,
        };

        // EntrySequence is SequenceNumber, or the hole number when that's null; if those repeat, the detail order.
        var sequences = details.Select(d => d.SequenceNumber ?? tee.HolesById[d.HoleId].Number).ToList();
        var repeated = sequences.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).Order().ToList();
        if (repeated.Count > 0)
        {
            findings.Add(new EntrySequenceFallback(legacy.Id, playedOn, repeated));
            sequences = Enumerable.Range(1, details.Count).ToList();
        }

        for (var i = 0; i < details.Count; i++)
        {
            var detail = details[i];
            var hole = tee.HolesById[detail.HoleId];
            round.HoleResults.Add(new HoleResult
            {
                Id = DeterministicIds.For("RoundDetail", detail.Id),
                RoundId = round.Id,
                HoleNumber = hole.Number,
                EntrySequence = sequences[i],
                Score = detail.Score,
                Putts = detail.Putts,
                FairwayHit = detail.FairwayHit,
                TeeShotLeft = detail.TeeShotLeft,
                TeeShotRight = detail.TeeShotRight,
                TeeShotLong = detail.TeeShotLong,
                TeeShotShort = detail.TeeShotShort,
                Gir = detail.GreenInRegulation,
                PenaltyStrokes = detail.PenaltyShots,
                GreensideSandShots = detail.SandShots,
                FairwaySandShots = 0,
                IsDerived = false,
            });

            if (detail.Putts == 0 && detail.Score > 1)
            {
                var looksLikeHoleOut = detail.GreenInRegulation == (detail.Score <= hole.Par - 2);
                findings.Add(new ZeroPuttHole(
                    legacy.Id, detail.Id, playedOn, hole.Number, hole.Par, detail.Score, detail.GreenInRegulation, looksLikeHoleOut));
            }
        }

        round.HoleResults.Sort((a, b) => a.EntrySequence.CompareTo(b.EntrySequence));
        return round;
    }

    private static RoundType MapRoundType(LegacyData data, LegacyRound legacy, DateOnly playedOn, List<MigrationFinding> findings)
    {
        if (legacy.RoundTypeId is not { } id)
        {
            return RoundType.Casual;
        }

        var name = data.RoundTypes.GetValueOrDefault(id);
        var match = Enum.GetValues<RoundType>()
            .Where(t => string.Equals(t.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(t => (RoundType?)t)
            .FirstOrDefault();
        if (match is null)
        {
            findings.Add(new UnknownRoundType(legacy.Id, playedOn, id, name));
        }

        return match ?? RoundType.Casual;
    }

    /// <summary><c>"&lt;weather&gt;; &lt;wind&gt;; &lt;temperature&gt;"</c> from the lookup names, skipping missing parts.</summary>
    private static string? LegacyWeather(LegacyData data, LegacyRound legacy)
    {
        string? Name(IReadOnlyDictionary<int, string> lookup, int? id) =>
            id is { } key ? CourseRules.Clean(lookup.GetValueOrDefault(key)) : null;

        var parts = new[]
            {
                Name(data.WeatherConditions, legacy.WeatherConditionId),
                Name(data.WindConditions, legacy.WindConditionId),
                Name(data.Temperatures, legacy.TemperatureId),
            }
            .OfType<string>()
            .ToList();
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>
    /// Minutes from <c>h:mm</c>, <c>hh:mm:ss</c> (seconds rounded to the nearest minute) or a plain number of minutes.
    /// Anything else, or a duration that isn't positive, is dropped and reported; a blank duration is just null.
    /// </summary>
    private static int? Duration(LegacyRound legacy, DateOnly playedOn, List<MigrationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(legacy.Duration))
        {
            return null;
        }

        var minutes = ParseDuration(legacy.Duration.Trim());
        if (minutes is > 0)
        {
            return minutes;
        }

        findings.Add(new RoundValueDropped(legacy.Id, playedOn, "Duration", legacy.Duration));
        return null;
    }

    private static int? ParseDuration(string text)
    {
        var parts = text.Split(':');
        if (parts.Length > 3 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        var numbers = parts.Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1).ToList();
        if (numbers.Any(n => n < 0) || numbers.Skip(1).Any(n => n > 59))
        {
            return null;
        }

        return numbers.Count switch
        {
            1 => numbers[0],
            2 => numbers[0] * 60 + numbers[1],
            _ => numbers[0] * 60 + numbers[1] + (numbers[2] >= 30 ? 1 : 0),
        };
    }

    private static decimal? Distance(LegacyRound legacy, DateOnly playedOn, List<MigrationFinding> findings)
    {
        if (legacy.Distance is not { } distance)
        {
            return null;
        }

        var rounded = Math.Round(distance, 2);
        if (rounded is >= 0 and <= MaxDistanceMiles)
        {
            return rounded;
        }

        findings.Add(new RoundValueDropped(legacy.Id, playedOn, "Distance", distance.ToString(CultureInfo.InvariantCulture)));
        return null;
    }

    /// <summary>Rounds on the same date and tee with the same hole-by-hole scores (in playing order).</summary>
    private static IEnumerable<MigrationFinding> DuplicateRounds(List<(LegacyRound Legacy, Round Round)> rounds) =>
        rounds
            .GroupBy(r => (
                r.Round.PlayedOn,
                r.Legacy.TeeId,
                Scores: string.Join(",", r.Round.HoleResults.Select(h => $"{h.HoleNumber}:{h.Score}"))))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateRounds(g.Key.PlayedOn, g.Key.TeeId, g.Select(r => r.Legacy.Id).ToList()));

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private sealed record MappedTee(TeeVersion Version, Dictionary<int, LegacyHole> HolesById);
}
