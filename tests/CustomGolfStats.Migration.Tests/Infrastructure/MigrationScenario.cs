using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Migration.Tests.Infrastructure;

/// <summary>
/// A synthetic golftrack database and an empty, migrated target with the golfer's account. Ann (golfer
/// <see cref="GolferId"/>) has, once imported:
/// <list type="bullet">
/// <item>2023-05-01: 18 holes at Pine Valley (League, Clear/Breezy/Mild, "4:10", 5.2 miles), every hole 5 with 2 putts.</item>
/// <item>2024-06-01 twice: identical 18-hole rounds at Pine Valley (duplicates), all 4s, hole 7 with 0 putts and GIR.</item>
/// <item>2024-07-01: 9 holes on Old Nine, an inactive course, tee and holes, round type "Scramble" (unknown), all 6s.</item>
/// </list>
/// and two rounds that are not imported: 2024-08-01 with no details (skipped, reported) and an inactive one.
/// Handicaps 12.4 (2023-01-01) and 11.8 (2024-01-01). Bob (another golfer) has one round at Pine Valley.
/// </summary>
public sealed class MigrationScenario
{
    public const string AccountEmail = "Ann.Golfer@Example.test";

    private MigrationScenario(string source, string target, LegacySeed seed)
    {
        SourceConnectionString = source;
        TargetConnectionString = target;
        Seed = seed;
    }

    public string SourceConnectionString { get; }

    public string TargetConnectionString { get; }

    public LegacySeed Seed { get; }

    public int GolferId { get; private set; }

    public int OtherGolferId { get; private set; }

    public int PineValleyTeeId { get; private set; }

    public IReadOnlyList<int> PineValleyHoleIds { get; private set; } = [];

    public int InactiveRoundId { get; private set; }

    public Guid AccountId { get; private set; }

    public static async Task<MigrationScenario> CreateAsync(MsSqlFixture sql, PostgresFixture postgres)
    {
        var source = await sql.CreateDatabaseAsync();
        var scenario = new MigrationScenario(source, await postgres.CreateDatabaseAsync(), new LegacySeed(source));
        await scenario.SeedAsync();
        scenario.AccountId = await scenario.CreateAccountAsync(AccountEmail);
        return scenario;
    }

    public AppDbContext CreateTargetContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(TargetConnectionString).Options, TimeProvider.System);

    public async Task<Guid> CreateAccountAsync(string email)
    {
        await using var db = CreateTargetContext();
        var account = new Account
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = "Test",
        };
        db.Users.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task SeedAsync()
    {
        var seed = Seed;
        GolferId = await seed.GolferAsync("Ann");
        var bob = await seed.GolferAsync("Bob");
        OtherGolferId = bob;

        var pineValley = await seed.CourseAsync("Pine Valley", latitude: 40.1m, longitude: -86.2m);
        PineValleyTeeId = await seed.TeeAsync(pineValley);
        PineValleyHoleIds = await seed.HolesAsync(PineValleyTeeId);

        var oldNine = await seed.CourseAsync("Old Nine", isActive: false);
        var oldNineTee = await seed.TeeAsync(oldNine, "Front", par: 36, rating: null, slope: null, isActive: false);
        var oldNineHoles = new List<int>();
        for (var number = 1; number <= 9; number++)
        {
            oldNineHoles.Add(await seed.HoleAsync(oldNineTee, number, handicap: number * 2 - 1, isActive: false));
        }

        var league = await seed.LookupAsync("RoundType", "League");
        await seed.LookupAsync("RoundType", "Casual");
        var scramble = await seed.LookupAsync("RoundType", "Scramble");
        var clear = await seed.LookupAsync("WeatherCondition", "Clear");
        var breezy = await seed.LookupAsync("WindCondition", "Breezy");
        var mild = await seed.LookupAsync("Temperature", "Mild");

        var first = await seed.RoundAsync(
            GolferId, PineValleyTeeId, new DateTime(2023, 5, 1, 8, 0, 0), score: 90, roundTypeId: league, weatherId: clear,
            windId: breezy, temperatureId: mild, duration: "4:10", distance: 5.2m, notes: "First");
        await DetailsAsync(first, PineValleyHoleIds, score: 5);

        for (var copy = 0; copy < 2; copy++)
        {
            var round = await seed.RoundAsync(GolferId, PineValleyTeeId, new DateTime(2024, 6, 1), score: 72);
            for (var i = 0; i < 18; i++)
            {
                await seed.RoundDetailAsync(round, PineValleyHoleIds[i], score: 4, putts: i == 6 ? 0 : 2, gir: true);
            }
        }

        var nine = await seed.RoundAsync(GolferId, oldNineTee, new DateTime(2024, 7, 1), score: 54, roundTypeId: scramble, holesPlayed: 9);
        await DetailsAsync(nine, oldNineHoles, score: 6);

        await seed.RoundAsync(GolferId, PineValleyTeeId, new DateTime(2024, 8, 1));
        InactiveRoundId = await seed.RoundAsync(GolferId, PineValleyTeeId, new DateTime(2024, 9, 1), isActive: false);
        await DetailsAsync(InactiveRoundId, PineValleyHoleIds, score: 5);

        var bobs = await seed.RoundAsync(bob, PineValleyTeeId, new DateTime(2024, 6, 2));
        await DetailsAsync(bobs, PineValleyHoleIds, score: 7);

        await seed.HandicapAsync(GolferId, new DateTime(2023, 1, 1), 12.4m);
        await seed.HandicapAsync(GolferId, new DateTime(2024, 1, 1), 11.8m);
        await seed.HandicapAsync(bob, new DateTime(2024, 1, 1), 20.0m);
    }

    private async Task DetailsAsync(int roundId, IReadOnlyList<int> holeIds, int score)
    {
        foreach (var holeId in holeIds)
        {
            await Seed.RoundDetailAsync(roundId, holeId, score: score);
        }
    }
}
