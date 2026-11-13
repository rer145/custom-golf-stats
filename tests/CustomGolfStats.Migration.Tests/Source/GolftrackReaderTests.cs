using CustomGolfStats.Migration.Source;
using CustomGolfStats.Migration.Tests.Infrastructure;

namespace CustomGolfStats.Migration.Tests.Source;

public sealed class GolftrackReaderTests(MsSqlFixture sql) : IAsyncLifetime
{
    private string _connectionString = null!;
    private LegacySeed _seed = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await sql.CreateDatabaseAsync();
        _seed = new LegacySeed(_connectionString);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Reads_the_golfers_rows_with_their_values()
    {
        var golfer = await _seed.GolferAsync("Ann");
        var course = await _seed.CourseAsync(
            "Pine Valley", address2: "Suite 2", website: "https://pine.example.test", latitude: 39.787654m, longitude: -86.123456m);
        var tee = await _seed.TeeAsync(course, "Blue", gender: "F", rating: null, slope: null, frontRating: 34.4, frontSlope: null);
        var hole = await _seed.HoleAsync(tee, 1, par: 5, yardage: 512, handicap: 7);
        var league = await _seed.LookupAsync("RoundType", "League");
        var rain = await _seed.LookupAsync("WeatherCondition", "Rain");
        var windy = await _seed.LookupAsync("WindCondition", "Windy", isActive: false);
        var cold = await _seed.LookupAsync("Temperature", "Cold");
        var round = await _seed.RoundAsync(
            golfer, tee, new DateTime(2024, 6, 1, 14, 30, 0), score: 6, roundTypeId: league, weatherId: rain, windId: windy,
            temperatureId: cold, holesPlayed: 1, duration: "4:15", distance: 5.4321m, notes: "Wet");
        var detail = await _seed.RoundDetailAsync(
            round, hole, sequenceNumber: 10, score: 6, putts: 3, fairwayHit: true, teeShotLeft: true, teeShotRight: false,
            teeShotLong: true, teeShotShort: false, gir: true, penaltyShots: 1, sandShots: 2);
        var handicap = await _seed.HandicapAsync(golfer, new DateTime(2024, 5, 1), 12.3m);

        var data = await new GolftrackReader(_connectionString).ReadAsync(golfer, TestContext.Current.CancellationToken);

        Assert.Equal(golfer, data.GolferId);
        Assert.Equal(
            new LegacyCourse(course, "Pine Valley", "1 Fairway Dr", "Suite 2", "Springfield", "12345", "555-0100",
                "https://pine.example.test", 39.787654m, -86.123456m),
            Assert.Single(data.Courses));
        Assert.Equal(
            new LegacyTee(tee, course, "Blue", "F", 72, null, null, 36, 34.4, null, 36, 35.1, 126),
            Assert.Single(data.Tees));
        Assert.Equal(new LegacyHole(hole, tee, 1, 5, 512, 7), Assert.Single(data.Holes));
        Assert.Equal(
            new LegacyRound(round, golfer, tee, league, rain, windy, cold, 1, 6, "4:15", 5.4321m,
                new DateTime(2024, 6, 1, 14, 30, 0), "Wet"),
            Assert.Single(data.Rounds));
        Assert.Equal(
            new LegacyRoundDetail(detail, round, hole, 10, 6, 3, true, true, false, true, false, true, 1, 2),
            Assert.Single(data.RoundDetails));
        Assert.Equal(new LegacyHandicap(handicap, golfer, new DateTime(2024, 5, 1), 12.3m), Assert.Single(data.Handicaps));
        Assert.Equal("League", data.RoundTypes[league]);
        Assert.Equal("Rain", data.WeatherConditions[rain]);
        Assert.Equal("Windy", data.WindConditions[windy]); // lookups are names only, read whether active or not
        Assert.Equal("Cold", data.Temperatures[cold]);
    }

    [Fact]
    public async Task Returns_nothing_of_another_golfer()
    {
        var ann = await _seed.GolferAsync("Ann");
        var bob = await _seed.GolferAsync("Bob");
        var shared = await _seed.CourseAsync("Shared");
        var bobsOnly = await _seed.CourseAsync("Bob's Course");
        var sharedTee = await _seed.TeeAsync(shared);
        var bobsTee = await _seed.TeeAsync(bobsOnly);
        var sharedHoles = await _seed.HolesAsync(sharedTee, 2);
        await _seed.HolesAsync(bobsTee, 2);
        var annsRound = await _seed.RoundAsync(ann, sharedTee, new DateTime(2024, 6, 1));
        var annsDetail = await _seed.RoundDetailAsync(annsRound, sharedHoles[0]);
        var bobsRound = await _seed.RoundAsync(bob, sharedTee, new DateTime(2024, 6, 2));
        await _seed.RoundDetailAsync(bobsRound, sharedHoles[0]);
        await _seed.RoundAsync(bob, bobsTee, new DateTime(2024, 6, 3));
        var annsHandicap = await _seed.HandicapAsync(ann, new DateTime(2024, 1, 1), 10m);
        await _seed.HandicapAsync(bob, new DateTime(2024, 1, 1), 20m);

        var data = await new GolftrackReader(_connectionString).ReadAsync(ann, TestContext.Current.CancellationToken);

        Assert.Equal([shared], data.Courses.Select(c => c.Id));
        Assert.Equal([sharedTee], data.Tees.Select(t => t.Id));
        Assert.Equal(sharedHoles, data.Holes.Select(h => h.Id));
        Assert.Equal([annsRound], data.Rounds.Select(r => r.Id));
        Assert.Equal([annsDetail], data.RoundDetails.Select(d => d.Id));
        Assert.Equal([annsHandicap], data.Handicaps.Select(h => h.Id));
    }

    [Fact]
    public async Task Excludes_inactive_rows()
    {
        var golfer = await _seed.GolferAsync();
        var course = await _seed.CourseAsync();
        var tee = await _seed.TeeAsync(course);
        var activeHole = await _seed.HoleAsync(tee, 1);
        var inactiveHole = await _seed.HoleAsync(tee, 2, isActive: false);
        var round = await _seed.RoundAsync(golfer, tee, new DateTime(2024, 6, 1));
        var activeDetail = await _seed.RoundDetailAsync(round, activeHole);
        await _seed.RoundDetailAsync(round, inactiveHole, isActive: false);
        var inactiveRound = await _seed.RoundAsync(golfer, tee, new DateTime(2024, 6, 2), isActive: false);
        await _seed.RoundDetailAsync(inactiveRound, activeHole);

        // A round on an inactive tee is still read (the mapper reports it as missing its tee) but the tee is not.
        var inactiveTee = await _seed.TeeAsync(course, "Red", isActive: false);
        await _seed.HolesAsync(inactiveTee, 1);
        var roundOnInactiveTee = await _seed.RoundAsync(golfer, inactiveTee, new DateTime(2024, 6, 3));

        // Likewise an inactive course hides its tees and holes.
        var inactiveCourse = await _seed.CourseAsync("Closed", isActive: false);
        var teeOfInactiveCourse = await _seed.TeeAsync(inactiveCourse);
        await _seed.HolesAsync(teeOfInactiveCourse, 1);
        var roundOnInactiveCourse = await _seed.RoundAsync(golfer, teeOfInactiveCourse, new DateTime(2024, 6, 4));

        var data = await new GolftrackReader(_connectionString).ReadAsync(golfer, TestContext.Current.CancellationToken);

        Assert.Equal([round, roundOnInactiveTee, roundOnInactiveCourse], data.Rounds.Select(r => r.Id));
        Assert.Equal([activeDetail], data.RoundDetails.Select(d => d.Id));
        Assert.Equal([course], data.Courses.Select(c => c.Id));
        Assert.Equal([tee], data.Tees.Select(t => t.Id));
        Assert.Equal([activeHole], data.Holes.Select(h => h.Id));
    }

    [Fact]
    public async Task Unknown_golfer_reads_empty()
    {
        var data = await new GolftrackReader(_connectionString).ReadAsync(999, TestContext.Current.CancellationToken);

        Assert.Empty(data.Rounds);
        Assert.Empty(data.Courses);
        Assert.Empty(data.Handicaps);
    }
}
