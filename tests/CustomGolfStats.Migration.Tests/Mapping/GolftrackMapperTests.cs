using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;
using CustomGolfStats.Migration.Mapping;
using CustomGolfStats.Migration.Report;
using CustomGolfStats.Migration.Source;

namespace CustomGolfStats.Migration.Tests.Mapping;

public sealed class GolftrackMapperTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private readonly LegacyBuilder _legacy = new();

    private MappedImport Map() => GolftrackMapper.Map(_legacy.Build(), AccountId, Now);

    // ---- Courses, tees and holes ----

    [Fact]
    public void Maps_a_course_with_its_tee_version_and_holes()
    {
        var course = _legacy.Add(new LegacyCourse(
            _legacy.NextId(), " Pine Valley ", "1 Fairway Dr", "Suite 2", "Springfield", "12345", "555-0100",
            "https://pine.example.test", 39.787654m, -86.123456m));
        var tee = _legacy.Add(new LegacyTee(_legacy.NextId(), course.Id, "Blue", "F", 72, 70.15, 131, 36, 35.04, 129, 36, 35.1, 133));
        var holes = _legacy.Holes(tee, strokeIndex: n => 19 - n, par: n => n == 3 ? 3 : n == 5 ? 5 : 4);
        var round = _legacy.Round(tee);
        _legacy.Details(round, holes);

        var result = Map();

        var mapped = Assert.Single(result.Courses);
        Assert.Equal(DeterministicIds.For("Course", course.Id), mapped.Id);
        Assert.Equal("Pine Valley", mapped.Name);
        Assert.Null(mapped.ClubName);
        Assert.Equal("1 Fairway Dr, Suite 2", mapped.Address);
        Assert.Equal("Springfield", mapped.City);
        Assert.Null(mapped.Region);
        Assert.Null(mapped.Country);
        Assert.Equal("555-0100", mapped.Phone);
        Assert.Equal("https://pine.example.test", mapped.Website);
        Assert.Equal(39.787654, mapped.Latitude);
        Assert.Equal(-86.123456, mapped.Longitude);
        Assert.Equal(CourseStatus.Verified, mapped.Status);
        Assert.Equal(CourseSource.Golftrack, mapped.Source);
        Assert.Equal(course.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), mapped.ProviderCourseId);
        Assert.Equal(Now, mapped.LastVerifiedAt);
        Assert.Null(mapped.MergedIntoCourseId);
        Assert.Equal(AccountId, mapped.CreatedByAccountId);
        Assert.Equal(Now, mapped.CreatedAt);
        Assert.Equal(Now, mapped.UpdatedAt);

        var mappedTee = Assert.Single(mapped.Tees);
        Assert.Equal(DeterministicIds.For("Tee", tee.Id), mappedTee.Id);
        Assert.Equal(mapped.Id, mappedTee.CourseId);
        Assert.Equal("Blue", mappedTee.Name);
        Assert.Equal(Gender.Female, mappedTee.Gender);

        var version = Assert.Single(mappedTee.Versions);
        Assert.Equal(DeterministicIds.For("TeeVersion", tee.Id), version.Id);
        Assert.Equal(mappedTee.Id, version.TeeId);
        Assert.Equal(new DateOnly(1900, 1, 1), version.EffectiveFrom);
        Assert.Equal(72, version.Par);
        Assert.Equal(70.2m, version.Rating);
        Assert.Equal(131, version.Slope);
        Assert.Equal(36, version.FrontPar);
        Assert.Equal(35.0m, version.FrontRating);
        Assert.Equal(129, version.FrontSlope);
        Assert.Equal(36, version.BackPar);
        Assert.Equal(35.1m, version.BackRating);
        Assert.Equal(133, version.BackSlope);
        Assert.Equal(CourseSource.Golftrack, version.Source);
        Assert.Equal(AccountId, version.ChangedByAccountId);
        Assert.Equal(Now, version.ChangedAt);

        Assert.Equal(Enumerable.Range(1, 18), version.Holes.Select(h => h.Number));
        Assert.All(version.Holes, h => Assert.Equal(version.Id, h.TeeVersionId));
        var third = version.Holes.Single(h => h.Number == 3);
        Assert.Equal(DeterministicIds.For("Hole", holes[2].Id), third.Id);
        Assert.Equal(3, third.Par);
        Assert.Equal(400, third.Yardage);
        Assert.Equal(16, third.StrokeIndex);
        Assert.DoesNotContain(result.Findings, f => f is TeeRatingDefaulted or TeeSkipped);
    }

    [Fact]
    public void Course_without_both_coordinates_has_none_and_blank_text_becomes_null()
    {
        var course = _legacy.Add(new LegacyCourse(_legacy.NextId(), "Somewhere", "", "  ", "", "", "", " ", 40m, null));
        var tee = _legacy.Tee(course);
        _legacy.Details(_legacy.Round(tee), _legacy.Holes(tee));

        var mapped = Assert.Single(Map().Courses);

        Assert.Null(mapped.Latitude);
        Assert.Null(mapped.Longitude);
        Assert.Null(mapped.Address);
        Assert.Null(mapped.City);
        Assert.Null(mapped.Phone);
        Assert.Null(mapped.Website);
    }

    [Fact]
    public void Nine_hole_tee_with_odd_stroke_indexes_is_normalised_to_1_to_9()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course, "Nine");
        var holes = _legacy.Holes(tee, 9, strokeIndex: n => new[] { 9, 1, 17, 5, 13, 3, 15, 7, 11 }[n - 1]);
        _legacy.Details(_legacy.Round(tee), holes);

        var result = Map();

        var version = Assert.Single(Assert.Single(Assert.Single(result.Courses).Tees).Versions);
        Assert.Equal([5, 1, 9, 3, 7, 2, 8, 4, 6], version.Holes.OrderBy(h => h.Number).Select(h => h.StrokeIndex));
        Assert.Equal(36, version.Par);
        Assert.Null(version.FrontPar);
        Assert.Null(version.BackPar);
        var round = Assert.Single(result.Rounds);
        Assert.Equal(9, round.HoleResults.Count);
    }

    [Fact]
    public void Null_ratings_default_like_imports_and_are_reported()
    {
        var course = _legacy.Course("Rough Acres");
        var tee = _legacy.Add(new LegacyTee(_legacy.NextId(), course.Id, "Red", null, 72, null, null, 36, null, null, 36, null, null));
        _legacy.Details(_legacy.Round(tee), _legacy.Holes(tee));

        var result = Map();

        var version = Assert.Single(Assert.Single(Assert.Single(result.Courses).Tees).Versions);
        Assert.Equal(0m, version.Rating);
        Assert.Equal(113, version.Slope);
        Assert.Null(version.FrontRating);
        Assert.Null(version.FrontSlope);
        Assert.Null(version.BackRating);
        Assert.Null(version.BackSlope);
        Assert.Equal(Gender.Unspecified, Assert.Single(Assert.Single(result.Courses).Tees).Gender);
        Assert.Equal(
            [
                new TeeRatingDefaulted(tee.Id, "Rough Acres", "Red", "Rating", null, "0"),
                new TeeRatingDefaulted(tee.Id, "Rough Acres", "Red", "Slope", null, "113"),
            ],
            result.Findings.OfType<TeeRatingDefaulted>());
        Assert.Single(result.Rounds);
    }

    [Fact]
    public void Out_of_range_ratings_are_treated_as_missing_and_reported()
    {
        var course = _legacy.Course("Odd");
        var tee = _legacy.Add(new LegacyTee(_legacy.NextId(), course.Id, "Gold", "M", 72, 0, 0, 36, 35.0, 200, 36, 0.5, 120));
        _legacy.Details(_legacy.Round(tee), _legacy.Holes(tee));

        var result = Map();

        var version = Assert.Single(Assert.Single(Assert.Single(result.Courses).Tees).Versions);
        Assert.Equal(0m, version.Rating);
        Assert.Equal(113, version.Slope);
        Assert.Equal(35.0m, version.FrontRating);
        Assert.Null(version.FrontSlope);
        Assert.Null(version.BackRating);
        Assert.Equal(120, version.BackSlope);
        Assert.Equal(
            [
                new TeeRatingDefaulted(tee.Id, "Odd", "Gold", "Rating", "0", "0"),
                new TeeRatingDefaulted(tee.Id, "Odd", "Gold", "Slope", "0", "113"),
                new TeeRatingDefaulted(tee.Id, "Odd", "Gold", "FrontSlope", "200", null),
                new TeeRatingDefaulted(tee.Id, "Odd", "Gold", "BackRating", "0.5", null),
            ],
            result.Findings.OfType<TeeRatingDefaulted>());
    }

    [Fact]
    public void Identical_duplicate_hole_rows_use_the_active_one_without_a_finding()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var oldFourth = _legacy.Add(new LegacyHole(_legacy.NextId(), tee.Id, 4, 4, 400, 4, IsActive: false));
        var holes = _legacy.Holes(tee);
        var round = _legacy.Round(tee);
        _legacy.Details(round, holes.Take(3));
        var playedOnOldRow = _legacy.Detail(round, oldFourth, score: 6);
        _legacy.Details(round, holes.Skip(4));

        var result = Map();

        var version = Assert.Single(Assert.Single(Assert.Single(result.Courses).Tees).Versions);
        Assert.Equal(18, version.Holes.Count);
        Assert.Equal(DeterministicIds.For("Hole", holes[3].Id), Assert.Single(version.Holes, h => h.Number == 4).Id);
        var fourth = Assert.Single(Assert.Single(result.Rounds).HoleResults, h => h.Id == DeterministicIds.For("RoundDetail", playedOnOldRow.Id));
        Assert.Equal(4, fourth.HoleNumber);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Differing_duplicate_hole_rows_keep_the_lowest_id_and_are_reported()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = _legacy.Holes(tee);
        var extra = _legacy.Add(new LegacyHole(_legacy.NextId(), tee.Id, 4, 5, 999, 4, IsActive: true));
        var round = _legacy.Round(tee);
        _legacy.Details(round, holes);

        var result = Map();

        var version = Assert.Single(Assert.Single(Assert.Single(result.Courses).Tees).Versions);
        var fourth = Assert.Single(version.Holes, h => h.Number == 4);
        Assert.Equal(DeterministicIds.For("Hole", holes[3].Id), fourth.Id);
        Assert.Equal(400, fourth.Yardage);
        var duplicate = Assert.Single(result.Findings.OfType<DuplicateTeeHoleNumber>());
        Assert.Equal((tee.Id, 4, holes[3].Id), (duplicate.LegacyTeeId, duplicate.HoleNumber, duplicate.KeptLegacyHoleId));
        Assert.Equal([holes[3].Id, extra.Id], duplicate.LegacyHoleIds);
    }

    [Fact]
    public void Inactive_holes_are_imported()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = Enumerable.Range(1, 9)
            .Select(n => _legacy.Add(new LegacyHole(_legacy.NextId(), tee.Id, n, 4, 380, n, IsActive: false)))
            .ToList();
        _legacy.Details(_legacy.Round(tee), holes);

        var result = Map();

        Assert.Equal(9, Assert.Single(Assert.Single(Assert.Single(result.Courses).Tees).Versions).Holes.Count);
        Assert.Equal(9, Assert.Single(result.Rounds).HoleResults.Count);
    }

    [Fact]
    public void Holes_outside_the_api_ranges_are_reported_but_imported()
    {
        var course = _legacy.Course("Wild");
        var tee = _legacy.Tee(course, "Long");
        var holes = _legacy.Holes(tee, 2).ToList();
        var odd = _legacy.Add(new LegacyHole(_legacy.NextId(), tee.Id, 19, 7, 1200, 3, IsActive: true));
        var negative = _legacy.Add(new LegacyHole(_legacy.NextId(), tee.Id, 0, 2, -5, 4, IsActive: true));
        _legacy.Details(_legacy.Round(tee), holes.Append(odd).Append(negative));

        var result = Map();

        var version = Assert.Single(Assert.Single(Assert.Single(result.Courses).Tees).Versions);
        Assert.Equal(4, version.Holes.Count);
        Assert.Equal(
            [
                new HoleOutOfRange(tee.Id, "Wild", "Long", negative.Id, 0, "Number", 0),
                new HoleOutOfRange(tee.Id, "Wild", "Long", negative.Id, 0, "Par", 2),
                new HoleOutOfRange(tee.Id, "Wild", "Long", negative.Id, 0, "Yardage", -5),
                new HoleOutOfRange(tee.Id, "Wild", "Long", odd.Id, 19, "Number", 19),
                new HoleOutOfRange(tee.Id, "Wild", "Long", odd.Id, 19, "Par", 7),
                new HoleOutOfRange(tee.Id, "Wild", "Long", odd.Id, 19, "Yardage", 1200),
            ],
            result.Findings.OfType<HoleOutOfRange>());
        Assert.Single(result.Rounds);
    }

    [Fact]
    public void Tee_without_holes_is_skipped_and_its_rounds_too()
    {
        var course = _legacy.Course("Bare");
        var tee = _legacy.Tee(course, "Black");
        var round = _legacy.Round(tee);

        var result = Map();

        Assert.Empty(result.Courses);
        Assert.Empty(result.Rounds);
        Assert.Equal(new TeeSkipped(tee.Id, "Bare", "Black", "no holes"), Assert.Single(result.Findings.OfType<TeeSkipped>()));
        Assert.Equal(round.Id, Assert.Single(result.Findings.OfType<RoundSkipped>()).LegacyRoundId);
    }

    // ---- Rounds ----

    [Fact]
    public void Maps_a_normal_18_hole_round_field_by_field()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = _legacy.Holes(tee);
        var league = _legacy.RoundType("League");
        var round = _legacy.Round(
            tee, new DateTime(2024, 7, 4, 23, 59, 0), roundTypeId: league, duration: "4:05", distance: 5.4321m, notes: "Windy day");
        var details = holes.Select((hole, i) => _legacy.Add(new LegacyRoundDetail(
            _legacy.NextId(), round.Id, hole.Id, i + 1, Score: 4 + (i % 3), Putts: 1 + (i % 2), FairwayHit: i % 2 == 0,
            TeeShotLeft: i == 1, TeeShotRight: i == 3, TeeShotLong: i == 5, TeeShotShort: i == 7, GreenInRegulation: i % 4 == 0,
            PenaltyShots: i == 9 ? 2 : 0, SandShots: i == 11 ? 1 : 0))).ToList();

        var result = Map();

        var mapped = Assert.Single(result.Rounds);
        Assert.Equal(DeterministicIds.For("Round", round.Id), mapped.Id);
        Assert.Equal(AccountId, mapped.AccountId);
        Assert.Equal(DeterministicIds.For("TeeVersion", tee.Id), mapped.TeeVersionId);
        Assert.Equal(new DateOnly(2024, 7, 4), mapped.PlayedOn);
        Assert.Null(mapped.StartedAt);
        Assert.Null(mapped.FinishedAt);
        Assert.Equal(EntryMode.Quick, mapped.EntryMode);
        Assert.Equal(RoundType.League, mapped.RoundType);
        Assert.Equal("Windy day", mapped.Notes);
        Assert.Null(mapped.WeatherConditions);
        Assert.Null(mapped.TemperatureF);
        Assert.Null(mapped.WindSpeedMph);
        Assert.Null(mapped.WindDirectionDegrees);
        Assert.Null(mapped.LegacyWeather);
        Assert.Equal(245, mapped.DurationMinutes);
        Assert.Equal(5.43m, mapped.DistanceMiles);
        Assert.Null(mapped.StepCount);
        Assert.Equal(Now, mapped.ClientUpdatedAt);
        Assert.Equal(Now, mapped.ModifiedAt);
        Assert.Null(mapped.DeletedAt);
        Assert.Equal(1, mapped.Version);
        Assert.False(mapped.TrialCounted);

        Assert.Equal(18, mapped.HoleResults.Count);
        for (var i = 0; i < 18; i++)
        {
            var detail = details[i];
            var hole = mapped.HoleResults[i];
            Assert.Equal(DeterministicIds.For("RoundDetail", detail.Id), hole.Id);
            Assert.Equal(mapped.Id, hole.RoundId);
            Assert.Equal(i + 1, hole.HoleNumber);
            Assert.Equal(i + 1, hole.EntrySequence);
            Assert.Equal(detail.Score, hole.Score);
            Assert.Equal(detail.Putts, hole.Putts);
            Assert.Equal(detail.FairwayHit, hole.FairwayHit);
            Assert.Equal(detail.TeeShotLeft, hole.TeeShotLeft);
            Assert.Equal(detail.TeeShotRight, hole.TeeShotRight);
            Assert.Equal(detail.TeeShotLong, hole.TeeShotLong);
            Assert.Equal(detail.TeeShotShort, hole.TeeShotShort);
            Assert.Equal(detail.GreenInRegulation, hole.Gir);
            Assert.Equal(detail.PenaltyShots, hole.PenaltyStrokes);
            Assert.Equal(detail.SandShots, hole.GreensideSandShots);
            Assert.Equal(0, hole.FairwaySandShots);
            Assert.False(hole.IsDerived);
            Assert.Empty(hole.Shots);
        }

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Back_nine_start_keeps_golftrack_sequence_numbers()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = _legacy.Holes(tee);
        var round = _legacy.Round(tee);
        foreach (var hole in holes)
        {
            _legacy.Detail(round, hole, sequence: hole.Number >= 10 ? hole.Number - 9 : hole.Number + 9);
        }

        var mapped = Assert.Single(Map().Rounds);

        Assert.Equal(1, mapped.HoleResults.Single(h => h.HoleNumber == 10).EntrySequence);
        Assert.Equal(18, mapped.HoleResults.Single(h => h.HoleNumber == 9).EntrySequence);
    }

    [Fact]
    public void Null_sequence_number_uses_the_hole_number()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = _legacy.Holes(tee);
        var round = _legacy.Round(tee);
        foreach (var hole in holes.Reverse())
        {
            _legacy.Detail(round, hole, sequence: hole.Number == 18 ? 18 : null);
        }

        var result = Map();

        var mapped = Assert.Single(result.Rounds);
        Assert.All(mapped.HoleResults, h => Assert.Equal(h.HoleNumber, h.EntrySequence));
        Assert.Empty(result.Findings.OfType<EntrySequenceFallback>());
    }

    [Fact]
    public void Duplicated_sequence_numbers_fall_back_to_detail_order_and_are_reported()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = _legacy.Holes(tee, 3);
        var round = _legacy.Round(tee);
        _legacy.Detail(round, holes[2], sequence: 1);
        _legacy.Detail(round, holes[0], sequence: 1);
        _legacy.Detail(round, holes[1], sequence: 2);

        var result = Map();

        var mapped = Assert.Single(result.Rounds);
        Assert.Equal([(3, 1), (1, 2), (2, 3)], mapped.HoleResults.Select(h => (h.HoleNumber, h.EntrySequence)));
        var fallback = Assert.Single(result.Findings.OfType<EntrySequenceFallback>());
        Assert.Equal((round.Id, new DateOnly(2024, 6, 1)), (fallback.LegacyRoundId, fallback.PlayedOn));
        Assert.Equal([1], fallback.RepeatedSequences);
    }

    [Fact]
    public void Round_that_plays_a_hole_twice_falls_back_to_detail_order()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course, "Nine");
        var holes = _legacy.Holes(tee, 9);
        var round = _legacy.Round(tee);
        _legacy.Details(round, holes.Concat(holes));

        var result = Map();

        var mapped = Assert.Single(result.Rounds);
        Assert.Equal(Enumerable.Range(1, 18), mapped.HoleResults.Select(h => h.EntrySequence));
        Assert.Equal(Enumerable.Range(1, 9).Concat(Enumerable.Range(1, 9)), mapped.HoleResults.Select(h => h.HoleNumber));
        Assert.Equal(Enumerable.Range(1, 9), Assert.Single(result.Findings.OfType<EntrySequenceFallback>()).RepeatedSequences);
    }

    [Fact]
    public void Round_that_plays_a_hole_twice_with_sequence_numbers_keeps_them()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course, "Nine");
        var holes = _legacy.Holes(tee, 9);
        var round = _legacy.Round(tee);
        var sequence = 1;
        foreach (var hole in holes.Concat(holes))
        {
            _legacy.Detail(round, hole, sequence: sequence++);
        }

        var result = Map();

        Assert.Equal(Enumerable.Range(1, 18), Assert.Single(result.Rounds).HoleResults.Select(h => h.EntrySequence));
        Assert.Empty(result.Findings.OfType<EntrySequenceFallback>());
    }

    [Fact]
    public void Round_with_a_missing_hole_is_skipped_and_reported()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = _legacy.Holes(tee);
        var round = _legacy.Round(tee, new DateTime(2023, 5, 6));
        _legacy.Details(round, holes.Take(17));
        _legacy.Add(new LegacyRoundDetail(_legacy.NextId(), round.Id, 99999, null, 4, 2, false, false, false, false, false, false, 0, 0));
        var good = _legacy.Round(tee);
        _legacy.Details(good, holes);

        var result = Map();

        Assert.Equal([DeterministicIds.For("Round", good.Id)], result.Rounds.Select(r => r.Id));
        var skipped = Assert.Single(result.Findings.OfType<RoundSkipped>());
        Assert.Equal(round.Id, skipped.LegacyRoundId);
        Assert.Equal(new DateOnly(2023, 5, 6), skipped.PlayedOn);
        Assert.Contains("99999", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Round_on_a_hole_of_another_tee_is_skipped()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var other = _legacy.Tee(course, "Red");
        var holes = _legacy.Holes(tee);
        var otherHoles = _legacy.Holes(other);
        var round = _legacy.Round(tee);
        _legacy.Details(round, holes.Take(17).Append(otherHoles[17]));

        var result = Map();

        Assert.Empty(result.Rounds);
        Assert.Equal(round.Id, Assert.Single(result.Findings.OfType<RoundSkipped>()).LegacyRoundId);
    }

    [Fact]
    public void Round_with_a_missing_tee_or_no_holes_is_skipped_and_reported()
    {
        var course = _legacy.Course();
        var tee = _legacy.Tee(course);
        var holes = _legacy.Holes(tee);
        var phantomTee = new LegacyTee(55555, course.Id, "Gone", "M", 72, 70, 120, 36, null, null, 36, null, null);
        var onMissingTee = _legacy.Round(phantomTee);
        var withoutHoles = _legacy.Round(tee);
        _legacy.Details(_legacy.Round(tee), holes);

        var result = Map();

        Assert.Single(result.Rounds);
        var skipped = result.Findings.OfType<RoundSkipped>().ToList();
        Assert.Equal([onMissingTee.Id, withoutHoles.Id], skipped.Select(s => s.LegacyRoundId));
        Assert.Contains("55555", skipped[0].Reason, StringComparison.Ordinal);
        Assert.Contains("no active hole details", skipped[1].Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Practice", RoundType.Practice)]
    [InlineData("Casual", RoundType.Casual)]
    [InlineData("Tournament", RoundType.Tournament)]
    [InlineData("League", RoundType.League)]
    [InlineData(" league ", RoundType.League)]
    public void Round_type_is_mapped_by_name(string name, RoundType expected)
    {
        var tee = _legacy.Tee(_legacy.Course());
        _legacy.Details(_legacy.Round(tee, roundTypeId: _legacy.RoundType(name)), _legacy.Holes(tee));

        var result = Map();

        Assert.Equal(expected, Assert.Single(result.Rounds).RoundType);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Unknown_round_type_becomes_casual_and_is_reported()
    {
        var tee = _legacy.Tee(_legacy.Course());
        var holes = _legacy.Holes(tee);
        var scramble = _legacy.RoundType("Scramble");
        var numeric = _legacy.RoundType("2");
        var named = _legacy.Round(tee, roundTypeId: scramble);
        var number = _legacy.Round(tee, roundTypeId: numeric);
        var dangling = _legacy.Round(tee, roundTypeId: 4242);
        var none = _legacy.Round(tee);
        foreach (var round in new[] { named, number, dangling, none })
        {
            _legacy.Details(round, holes, score: i => round.Id % 7 + i % 3);
        }

        var result = Map();

        Assert.All(result.Rounds, r => Assert.Equal(RoundType.Casual, r.RoundType));
        Assert.Equal(
            [
                new UnknownRoundType(named.Id, new DateOnly(2024, 6, 1), scramble, "Scramble"),
                new UnknownRoundType(number.Id, new DateOnly(2024, 6, 1), numeric, "2"),
                new UnknownRoundType(dangling.Id, new DateOnly(2024, 6, 1), 4242, null),
            ],
            result.Findings.OfType<UnknownRoundType>());
    }

    [Fact]
    public void Legacy_weather_joins_the_lookup_names_and_skips_missing_parts()
    {
        var tee = _legacy.Tee(_legacy.Course());
        var holes = _legacy.Holes(tee);
        var rain = _legacy.Weather("Rain");
        var windy = _legacy.Wind("Windy");
        var cold = _legacy.Temperature("Cold");
        var blank = _legacy.Wind("  ");
        var rounds = new[]
        {
            _legacy.Round(tee, new DateTime(2024, 1, 1), weatherId: rain, windId: windy, temperatureId: cold),
            _legacy.Round(tee, new DateTime(2024, 1, 2), weatherId: rain, temperatureId: cold),
            _legacy.Round(tee, new DateTime(2024, 1, 3), windId: windy),
            _legacy.Round(tee, new DateTime(2024, 1, 4), weatherId: 4242, windId: blank, temperatureId: cold),
            _legacy.Round(tee, new DateTime(2024, 1, 5)),
        };
        foreach (var round in rounds)
        {
            _legacy.Details(round, holes);
        }

        var result = Map();

        Assert.Equal(
            ["Rain; Windy; Cold", "Rain; Cold", "Windy", "Cold", null],
            result.Rounds.OrderBy(r => r.PlayedOn).Select(r => r.LegacyWeather));
    }

    [Theory]
    [InlineData("4:15", 255)]
    [InlineData("04:15", 255)]
    [InlineData("4:15:00", 255)]
    [InlineData("04:15:40", 256)]
    [InlineData("04:15:29", 255)]
    [InlineData("255", 255)]
    [InlineData(" 90 ", 90)]
    public void Duration_is_parsed(string duration, int expected)
    {
        var tee = _legacy.Tee(_legacy.Course());
        _legacy.Details(_legacy.Round(tee, duration: duration), _legacy.Holes(tee));

        var result = Map();

        Assert.Equal(expected, Assert.Single(result.Rounds).DurationMinutes);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("about four hours")]
    [InlineData("4:75")]
    [InlineData("4h15")]
    [InlineData("1:2:3:4")]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("99999999:00")]
    [InlineData("35791395:00")]
    [InlineData("99999999999")]
    public void Unparseable_duration_is_dropped_and_reported(string duration)
    {
        var tee = _legacy.Tee(_legacy.Course());
        var round = _legacy.Round(tee, duration: duration);
        _legacy.Details(round, _legacy.Holes(tee));

        var result = Map();

        Assert.Null(Assert.Single(result.Rounds).DurationMinutes);
        Assert.Equal(
            new RoundValueDropped(round.Id, new DateOnly(2024, 6, 1), "Duration", duration),
            Assert.Single(result.Findings));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_duration_and_notes_are_null_without_a_finding(string? value)
    {
        var tee = _legacy.Tee(_legacy.Course());
        _legacy.Details(_legacy.Round(tee, duration: value, notes: value), _legacy.Holes(tee));

        var result = Map();

        var round = Assert.Single(result.Rounds);
        Assert.Null(round.DurationMinutes);
        Assert.Null(round.Notes);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Distance_that_does_not_fit_is_dropped_and_reported()
    {
        var tee = _legacy.Tee(_legacy.Course());
        var round = _legacy.Round(tee, distance: 12345.5m);
        _legacy.Details(round, _legacy.Holes(tee));

        var result = Map();

        Assert.Null(Assert.Single(result.Rounds).DistanceMiles);
        Assert.Equal(new RoundValueDropped(round.Id, new DateOnly(2024, 6, 1), "Distance", "12345.5"), Assert.Single(result.Findings));
    }

    // ---- Handicaps ----

    [Fact]
    public void Handicap_history_becomes_handicap_entries()
    {
        var first = _legacy.Handicap(new DateTime(2023, 3, 1, 12, 0, 0), 14.2m);
        var second = _legacy.Handicap(new DateTime(2024, 3, 1), -1.5m);

        var result = Map();

        Assert.Equal(
            [
                (DeterministicIds.For("HandicapHistory", first.Id), AccountId, new DateOnly(2023, 3, 1), 14.2m),
                (DeterministicIds.For("HandicapHistory", second.Id), AccountId, new DateOnly(2024, 3, 1), -1.5m),
            ],
            result.HandicapEntries.Select(h => (h.Id, h.AccountId, h.EffectiveOn, h.Index)));
        Assert.Empty(result.Findings);
    }

    // ---- Report-only findings ----

    [Fact]
    public void Duplicate_rounds_are_all_imported_and_reported()
    {
        var tee = _legacy.Tee(_legacy.Course());
        var holes = _legacy.Holes(tee);
        var day = new DateTime(2026, 5, 2);
        var copies = Enumerable.Range(0, 4).Select(_ => _legacy.Round(tee, day)).ToList();
        foreach (var copy in copies)
        {
            _legacy.Details(copy, holes, score: i => 4 + i % 2);
        }

        var differentScores = _legacy.Round(tee, day);
        _legacy.Details(differentScores, holes, score: i => 5);
        var differentDay = _legacy.Round(tee, day.AddDays(1));
        _legacy.Details(differentDay, holes, score: i => 4 + i % 2);

        var result = Map();

        Assert.Equal(6, result.Rounds.Count);
        var duplicate = Assert.Single(result.Findings.OfType<DuplicateRounds>());
        Assert.Equal((new DateOnly(2026, 5, 2), tee.Id), (duplicate.PlayedOn, duplicate.LegacyTeeId));
        Assert.Equal(copies.Select(c => c.Id), duplicate.LegacyRoundIds);
    }

    [Fact]
    public void Zero_putt_holes_above_one_stroke_are_reported_with_whether_they_look_like_hole_outs()
    {
        var tee = _legacy.Tee(_legacy.Course());
        var holes = _legacy.Holes(tee, 6, par: n => n == 1 ? 3 : n == 2 ? 5 : 4);
        var round = _legacy.Round(tee, new DateTime(2022, 8, 9));
        var ace = _legacy.Detail(round, holes[0], score: 1, putts: 0, gir: true);              // not reported: score 1
        var eagle = _legacy.Detail(round, holes[1], score: 3, putts: 0, gir: true);            // holed approach, GIR (3 <= 5 - 2)
        var chipIn = _legacy.Detail(round, holes[2], score: 3, putts: 0, gir: false);          // chip-in birdie, no GIR
        var notEntered = _legacy.Detail(round, holes[3], score: 4, putts: 0, gir: true);       // GIR on par yet no putts
        var oddEagle = _legacy.Detail(round, holes[4], score: 2, putts: 0, gir: false);        // eagle without GIR flag
        _legacy.Detail(round, holes[5], score: 4, putts: 2, gir: true);

        var result = Map();

        var date = new DateOnly(2022, 8, 9);
        Assert.Equal(
            [
                new ZeroPuttHole(round.Id, eagle.Id, date, 2, 5, 3, true, true),
                new ZeroPuttHole(round.Id, chipIn.Id, date, 3, 4, 3, false, true),
                new ZeroPuttHole(round.Id, notEntered.Id, date, 4, 4, 4, true, false),
                new ZeroPuttHole(round.Id, oddEagle.Id, date, 5, 4, 2, false, false),
            ],
            result.Findings.OfType<ZeroPuttHole>());
        Assert.Equal(2022, result.Findings.OfType<ZeroPuttHole>().First().Year);
        Assert.DoesNotContain(result.Findings.OfType<ZeroPuttHole>(), f => f.LegacyRoundDetailId == ace.Id);
    }

    [Fact]
    public void Mapping_is_deterministic()
    {
        _legacy.StandardRound();
        _legacy.Handicap(new DateTime(2024, 1, 1), 10m);
        var data = _legacy.Build();

        var first = GolftrackMapper.Map(data, AccountId, Now);
        var second = GolftrackMapper.Map(data, AccountId, Now);

        Assert.Equal(first.Rounds.Select(r => r.Id), second.Rounds.Select(r => r.Id));
        Assert.Equal(
            first.Rounds.SelectMany(r => r.HoleResults).Select(h => h.Id),
            second.Rounds.SelectMany(r => r.HoleResults).Select(h => h.Id));
        Assert.Equal(
            first.Courses.SelectMany(c => c.Tees).SelectMany(t => t.Versions).SelectMany(v => v.Holes).Select(h => h.Id),
            second.Courses.SelectMany(c => c.Tees).SelectMany(t => t.Versions).SelectMany(v => v.Holes).Select(h => h.Id));
        Assert.Equal(first.HandicapEntries.Select(h => h.Id), second.HandicapEntries.Select(h => h.Id));
    }
}
