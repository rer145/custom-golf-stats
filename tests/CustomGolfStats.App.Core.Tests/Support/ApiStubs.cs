using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Platform;

namespace CustomGolfStats.App.Core.Tests.Support;

public sealed record Recorded(HttpMethod Method, string PathAndQuery, string? Bearer, string? Body)
{
    public string Path => PathAndQuery.Split('?')[0];
    public override string ToString() => $"{Method} {PathAndQuery}";
}

/// <summary>A stub server: the responder sees each recorded request and returns an OpenAPI-shaped response (or throws).</summary>
public sealed class StubHandler(Func<Recorded, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly object _lock = new();
    public List<Recorded> Requests { get; } = [];

    public static readonly Uri BaseAddress = new("https://api.test/");

    public CustomGolfStatsApiClient Client() => new(new HttpClient(this, disposeHandler: false) { BaseAddress = BaseAddress });

    public IEnumerable<Recorded> To(string method, string pathPrefix) =>
        Requests.Where(r => r.Method.Method == method && r.Path.StartsWith(pathPrefix, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new Recorded(request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter, body);
        lock (_lock)
            Requests.Add(recorded);
        return respond(recorded);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object? body = null) => new(status)
    {
        Content = new StringContent(body is null ? "" : JsonSerializer.Serialize(body, body.GetType()), Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
    };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);
}

public sealed class FakeSecureStore : ISecureStore
{
    public Dictionary<string, string> Values { get; } = [];
    public Task<string?> GetAsync(string key) => Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);
    public Task SetAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
    public Task RemoveAsync(string key) { Values.Remove(key); return Task.CompletedTask; }
}

/// <summary>Builders for server payloads.</summary>
public static class Payloads
{
    public static RoundDto Round(Guid id, int version, string notes = "from server", DateTimeOffset? deletedAt = null) => new()
    {
        Id = id, CourseId = Samples.CourseId, CourseName = "Pine Valley", TeeId = Samples.TeeId, TeeName = "Blue", TeeVersionId = Samples.TeeVersionId,
        PlayedOn = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), EntryMode = Api.Client.EntryMode.Quick, RoundType = RoundType.Casual,
        Notes = notes, ClientUpdatedAt = new DateTimeOffset(2026, 6, 1, 13, 0, 0, TimeSpan.Zero),
        ModifiedAt = new DateTimeOffset(2026, 6, 1, 13, 1, 0, TimeSpan.Zero), Version = version, DeletedAt = deletedAt,
        Holes = deletedAt is null ? [new HoleResultDto { HoleNumber = 1, EntrySequence = 1, Par = 4, Score = 5, Putts = 2 }] : [],
    };

    public static SyncChangesResponse Changes(DateTimeOffset nextSince, params RoundDto[] rounds) => new()
    {
        ServerTime = nextSince, NextSince = nextSince, Rounds = rounds,
        Entitlements = new EntitlementsResponse { Tier = "Free", Role = "User", Features = new EntitlementFeatures { ShotByShotEntry = true }, TrialShotRoundsRemaining = 3 },
    };

    public static AccessTokenResponse Tokens(string access, string refresh) =>
        new() { TokenType = "Bearer", AccessToken = access, RefreshToken = refresh, ExpiresIn = 3600 };

    public static SyncCourseDto Course() => new()
    {
        Id = Samples.CourseId, Name = "Pine Valley", Status = CourseStatus.Verified, Source = CourseSource.GolfCourseApi,
        UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Tees = [new SyncTeeDto { Id = Samples.TeeId, Name = "Blue", Gender = Gender.Male, Versions = [new SyncTeeVersionDto { Id = Samples.TeeVersionId, EffectiveFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), Par = 72, Rating = 71, Slope = 130, Holes = [new TeeHoleDto { Number = 1, Par = 4, Yardage = 400, StrokeIndex = 1 }] }] }],
    };
}
