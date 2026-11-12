using System.Net.Http.Json;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Rounds;
using CustomGolfStats.Api.Tests.Infrastructure;
using CustomGolfStats.Domain;

namespace CustomGolfStats.Api.Tests.Rounds;

/// <summary>Builds round push requests for the tests.</summary>
public static class RoundRequests
{
    public static readonly DateOnly PlayedOn = new(2026, 5, 30);

    /// <summary>A quick hole: <paramref name="score"/> with 2 putts, nothing else.</summary>
    public static UpsertHoleRequest QuickHole(int number, int score, int? sequence = null, int putts = 2) =>
        new(number, sequence ?? number, score, putts, null, null, null, null, null, null, null, null, null, null);

    public static UpsertRoundRequest Quick(Guid teeId, DateTimeOffset clientUpdatedAt, params UpsertHoleRequest[] holes) =>
        Round(teeId, EntryMode.Quick, clientUpdatedAt, holes);

    public static UpsertRoundRequest ShotByShot(Guid teeId, DateTimeOffset clientUpdatedAt, params UpsertHoleRequest[] holes) =>
        Round(teeId, EntryMode.ShotByShot, clientUpdatedAt, holes);

    public static UpsertRoundRequest Round(Guid teeId, EntryMode mode, DateTimeOffset clientUpdatedAt, IReadOnlyList<UpsertHoleRequest> holes) =>
        new(teeId, PlayedOn, null, null, mode, RoundType.Casual, null, null, null, null, null, null, null, null, clientUpdatedAt, holes);

    /// <summary>
    /// A par-4 hole played in four strokes plus <paramref name="extraPutts"/>: drive to the fairway (left), approach to
    /// the green, two putts. Summary fields carry nonsense the server must ignore.
    /// </summary>
    public static UpsertHoleRequest ShotHole(int number, int sequence, IReadOnlyList<ShotDto>? shots = null) =>
        new(number, sequence, 99, 42, false, false, true, true, true, false, 7, 7, 7, shots ?? ParFourShots());

    public static ShotDto[] ParFourShots() =>
    [
        new(1, Lie.Tee, 380, Club.Driver, ShotType.Tee, Lie.Fairway, 140, Direction.Left, Shape.Draw, 0),
        new(2, Lie.Fairway, 140, Club.Iron8, ShotType.Approach, Lie.Green, 20, Direction.OnTarget, null, 0),
        new(3, Lie.Green, 20, Club.Putter, ShotType.Putt, Lie.Green, 3, Direction.Short, null, 0),
        new(4, Lie.Green, 3, Club.Putter, ShotType.Putt, Lie.Holed, null, Direction.OnTarget, null, 0),
    ];

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid roundId, UpsertRoundRequest request) =>
        client.PutAsJsonAsync($"/rounds/{roundId}", request, TestContext.Current.CancellationToken);

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(TestJson.Options, TestContext.Current.CancellationToken);
        return value ?? throw new InvalidOperationException("Empty response body.");
    }
}
