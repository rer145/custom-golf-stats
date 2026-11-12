using CustomGolfStats.Api.Client;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;

namespace CustomGolfStats.Api.Tests.OpenApi;

public sealed class GeneratedClientTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Generated_client_registers_logs_in_and_reads_the_profile()
    {
        var ct = TestContext.Current.CancellationToken;
        var http = factory.CreateClient();
        var api = new CustomGolfStatsApiClient(http);
        const string email = "client-smoke@example.com";

        await api.AuthRegisterAsync(new RegisterRequest { Email = email, Password = TestAuth.Password }, ct);
        var tokens = await api.AuthLoginAsync(null, null, new LoginRequest { Email = email, Password = TestAuth.Password }, ct);
        http.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        var profile = await api.MeGetProfileAsync(ct);

        Assert.Equal(email, profile.Email);
    }

    [Fact]
    public async Task Generated_client_throws_ApiException_with_the_status_code_when_not_signed_in()
    {
        var api = new CustomGolfStatsApiClient(factory.CreateClient());

        var error = await Assert.ThrowsAsync<ApiException>(() => api.MeGetProfileAsync(TestContext.Current.CancellationToken));

        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task Generated_client_round_trips_enum_values_in_a_shot_by_shot_round()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "client-enums@example.com";
        var http = await TestAuth.RegisterAsync(factory, email);
        await TestAuth.SetAsync(factory, email, account => account.Tier = Domain.Tier.Paid);
        var seeded = await factory.WithDbAsync(db => TestData.SeedCourseAsync(db, "Client Enums"));
        var api = new CustomGolfStatsApiClient(http);
        var roundId = Guid.NewGuid();

        var request = new UpsertRoundRequest
        {
            TeeId = seeded.Tee.Id,
            PlayedOn = new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero),
            EntryMode = Client.EntryMode.ShotByShot,
            RoundType = Client.RoundType.Casual,
            ClientUpdatedAt = ApiFactory.StartTime,
            Holes =
            [
                new UpsertHoleRequest
                {
                    HoleNumber = 1,
                    EntrySequence = 1,
                    Shots =
                    [
                        new ShotDto { Sequence = 1, StartLie = Client.Lie.Tee, StartDistance = 380, Club = Client.Club.Driver, ShotType = Client.ShotType.Tee, EndLie = Client.Lie.Fairway, EndDistance = 140, Direction = Client.Direction.Left, Shape = Client.Shape.Draw, PenaltyStrokes = 0 },
                        new ShotDto { Sequence = 2, StartLie = Client.Lie.Fairway, StartDistance = 140, Club = Client.Club.Iron8, ShotType = Client.ShotType.Approach, EndLie = Client.Lie.Green, EndDistance = 20, Direction = Client.Direction.OnTarget, PenaltyStrokes = 0 },
                        new ShotDto { Sequence = 3, StartLie = Client.Lie.Green, StartDistance = 20, Club = Client.Club.Putter, ShotType = Client.ShotType.Putt, EndLie = Client.Lie.Green, EndDistance = 3, Direction = Client.Direction.Short, PenaltyStrokes = 0 },
                        new ShotDto { Sequence = 4, StartLie = Client.Lie.Green, StartDistance = 3, Club = Client.Club.Putter, ShotType = Client.ShotType.Putt, EndLie = Client.Lie.Holed, Direction = Client.Direction.OnTarget, PenaltyStrokes = 0 },
                    ],
                },
            ],
        };

        await api.RoundsUpsertAsync(roundId, request, ct);
        var round = await api.RoundsGetAsync(roundId, ct);

        Assert.Equal(Client.EntryMode.ShotByShot, round.EntryMode);
        var shots = Assert.Single(round.Holes).Shots!.ToList();
        Assert.Equal(Client.Club.Driver, shots[0].Club);
        Assert.Equal(Client.Lie.Tee, shots[0].StartLie);
        Assert.Equal(Client.Direction.Left, shots[0].Direction);
        Assert.Equal(Client.Shape.Draw, shots[0].Shape);
        Assert.Null(shots[1].Shape);
        Assert.Equal(Client.Lie.Holed, shots[3].EndLie);
    }
}
