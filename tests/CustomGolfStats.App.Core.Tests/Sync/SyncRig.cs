using System.Net;
using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.App.Core.Tests.Support;

namespace CustomGolfStats.App.Core.Tests.Sync;

/// <summary>A temp database, a stub server with overridable routes, and a SyncEngine wired to them.</summary>
public sealed class SyncRig : IAsyncDisposable
{
    public static readonly DateTimeOffset Cursor1 = new(2026, 6, 10, 8, 0, 0, TimeSpan.Zero);

    public TempDb Temp { get; private init; } = null!;
    public StubHandler Server { get; private init; } = null!;
    public SyncEngine Engine { get; private init; } = null!;

    public Func<Recorded, HttpResponseMessage> PutRound { get; set; } = r => StubHandler.Json(HttpStatusCode.OK, Payloads.Round(IdOf(r), 1, "pushed"));
    public Func<Recorded, HttpResponseMessage> DeleteRound { get; set; } = _ => StubHandler.Status(HttpStatusCode.NoContent);
    public Func<Recorded, HttpResponseMessage> PostCourse { get; set; } = _ => StubHandler.Json(HttpStatusCode.Created, new CourseDetailDto { Id = Samples.CourseId, Name = "x" });
    public Func<Recorded, HttpResponseMessage> Changes { get; set; } = _ => StubHandler.Json(HttpStatusCode.OK, Payloads.Changes(Cursor1));

    public static async Task<SyncRig> CreateAsync(Func<StubHandler, ICustomGolfStatsApiClient>? clientFactory = null)
    {
        var temp = await TempDb.CreateAsync();
        SyncRig? rig = null;
        var handler = new StubHandler(r => rig!.Dispatch(r));
        rig = new SyncRig
        {
            Temp = temp, Server = handler,
            Engine = new SyncEngine(temp.Db, clientFactory?.Invoke(handler) ?? handler.Client()),
        };
        await rig.SeedCourseAsync();
        return rig;
    }

    private async Task SeedCourseAsync() => await Temp.Db.Courses.SaveFromServerAsync(Samples.Course());

    private HttpResponseMessage Dispatch(Recorded r) => (r.Method.Method, r.Path) switch
    {
        ("PUT", var p) when p.StartsWith("/rounds/") => PutRound(r),
        ("DELETE", var p) when p.StartsWith("/rounds/") => DeleteRound(r),
        ("POST", "/courses") => PostCourse(r),
        ("GET", "/sync/changes") => Changes(r),
        _ => StubHandler.Status(HttpStatusCode.NotFound),
    };

    public static Guid IdOf(Recorded r) => Guid.Parse(r.Path.Split('/')[2]);

    public Task<SyncResult> SyncAsync() => Engine.SyncAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => Temp.DisposeAsync();
}

