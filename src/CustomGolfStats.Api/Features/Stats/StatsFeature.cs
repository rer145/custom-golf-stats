using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Domain.Stats;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Features.Stats;

public static class StatsFeature
{
    public static IServiceCollection AddStatsFeature(this IServiceCollection services)
    {
        services.AddScoped<RoundDataLoader>();
        return services;
    }

    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stats").WithTags("Stats").RequireAuthorization();

        group.MapGet("/years", GetYears).WithName("StatsYears");
        group.MapGet("/career", GetCareer).WithName("StatsCareer");
        group.MapGet("/courses", GetCourses).WithName("StatsCourses");
        group.MapGet("/handicap", GetHandicap).WithName("StatsHandicap");
        group.MapGet("/{year:int}/overall", GetOverall).WithName("StatsOverall");
        group.MapGet("/{year:int}/season", GetSeason).WithName("StatsSeason");
        group.MapGet("/{year:int}/results", GetResults).WithName("StatsResults");
        group.MapGet("/{year:int}/scoring", GetScoring).WithName("StatsScoring");
        group.MapGet("/{year:int}/off-the-tee", GetOffTheTee).WithName("StatsOffTheTee");
        group.MapGet("/{year:int}/approach", GetApproach).WithName("StatsApproach");
        group.MapGet("/{year:int}/putting", GetPutting).WithName("StatsPutting");

        var rounds = app.MapGroup("/rounds").WithTags("Stats").RequireAuthorization();
        rounds.MapGet("/{id:guid}/scorecard", GetScorecard).WithName("StatsScorecard");

        return app;
    }

    private static async Task<IReadOnlyList<RoundData>> YearAsync(
        HttpContext http, RoundDataLoader loader, int year, CancellationToken ct) =>
        SeasonStats.ForYear(await loader.LoadAsync(http.User.GetAccountId(), ct), year);

    private static async Task<Ok<StatsYearsResponse>> GetYears(
        HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var rounds = await loader.LoadAsync(http.User.GetAccountId(), ct);
        var years = rounds.Select(r => r.PlayedOn.Year).Distinct().OrderByDescending(y => y).ToList();
        return TypedResults.Ok(new StatsYearsResponse(years));
    }

    private static async Task<Ok<OverallResponse>> GetOverall(
        int year, HttpContext http, RoundDataLoader loader, AppDbContext db, CancellationToken ct)
    {
        var accountId = http.User.GetAccountId();
        var tiles = SeasonStats.Overall(await YearAsync(http, loader, year, ct));
        var yearEnd = new DateOnly(Math.Clamp(year, 1, 9999), 12, 31);
        var handicap = await db.HandicapEntries
            .AsNoTracking()
            .Where(h => h.AccountId == accountId && h.EffectiveOn <= yearEnd)
            .OrderByDescending(h => h.EffectiveOn)
            .FirstOrDefaultAsync(ct);
        return TypedResults.Ok(new OverallResponse(year, AverageDto.From(tiles.ScoringAverage18),
            tiles.HolesPlayed, tiles.ToPar, handicap?.Index, handicap?.EffectiveOn));
    }

    private static async Task<Ok<SeasonResponse>> GetSeason(
        int year, HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var s = SeasonStats.Season(await YearAsync(http, loader, year, ct));
        return TypedResults.Ok(new SeasonResponse(s.Rounds9, s.Rounds18, s.RoundsOtherLength,
            s.Histogram9.Select(b => new ScoreBucketDto(b.Min, b.Max, b.Count)).ToList(),
            s.Histogram18.Select(b => new ScoreBucketDto(b.Min, b.Max, b.Count)).ToList()));
    }

    private static async Task<Ok<ResultsResponse>> GetResults(
        int year, HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var rows = SeasonStats.Results(await YearAsync(http, loader, year, ct));
        return TypedResults.Ok(new ResultsResponse(rows.Select(r => new SeasonResultDto(
            r.RoundId, r.PlayedOn, r.CourseName, r.TeeName, r.HoleCount, r.Score, r.ToPar, r.Putts,
            RatioDto.From(r.Gir), RatioDto.From(r.DrivingAccuracy),
            r.Par3ToPar, r.Par4ToPar, r.Par5ToPar)).ToList()));
    }

    private static async Task<Ok<ScoringResponse>> GetScoring(
        int year, HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var s = ScoringStats.Calculate(await YearAsync(http, loader, year, ct));
        return TypedResults.Ok(new ScoringResponse(
            AverageDto.From(s.ScoringAverage9), AverageDto.From(s.ScoringAverage18),
            s.Lowest9, s.Highest9, s.Lowest18, s.Highest18, s.Birdies, s.Eagles, RatioDto.From(s.ParBreakers),
            AverageDto.From(s.Par3Average), AverageDto.From(s.Par4Average), AverageDto.From(s.Par5Average),
            RatioDto.From(s.BirdieOrBetter)));
    }

    private static async Task<Ok<OffTheTeeResponse>> GetOffTheTee(
        int year, HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var s = OffTheTeeStats.Calculate(await YearAsync(http, loader, year, ct));
        return TypedResults.Ok(new OffTheTeeResponse(
            RatioDto.From(s.DrivingAccuracy), RatioDto.From(s.LeftTendency), RatioDto.From(s.RightTendency),
            AverageDto.From(s.Par4ToParFromFairway), AverageDto.From(s.Par4ToParFromLeft), AverageDto.From(s.Par4ToParFromRight),
            AverageDto.From(s.Par5ToParFromFairway), AverageDto.From(s.Par5ToParFromLeft), AverageDto.From(s.Par5ToParFromRight)));
    }

    private static async Task<Ok<ApproachResponse>> GetApproach(
        int year, HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var s = ApproachStats.Calculate(await YearAsync(http, loader, year, ct));
        return TypedResults.Ok(new ApproachResponse(
            RatioDto.From(s.Gir), RatioDto.From(s.GirFromFairway), RatioDto.From(s.GirFromLeft),
            RatioDto.From(s.GirFromRight), RatioDto.From(s.SandSave), RatioDto.From(s.Scrambling),
            RatioDto.From(s.HoleOut)));
    }

    private static async Task<Ok<PuttingResponse>> GetPutting(
        int year, HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var s = PuttingStats.Calculate(await YearAsync(http, loader, year, ct));
        return TypedResults.Ok(new PuttingResponse(
            AverageDto.From(s.PuttsPerGir), AverageDto.From(s.PuttsPerHole), RatioDto.From(s.OnePutt),
            RatioDto.From(s.ThreePuttRate), AverageDto.From(s.PuttsPerRound9), AverageDto.From(s.PuttsPerRound18),
            RatioDto.From(s.BirdieConversion)));
    }

    private static async Task<Ok<CareerResponse>> GetCareer(
        HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        var rows = CareerStats.ByYear(await loader.LoadAsync(http.User.GetAccountId(), ct));
        return TypedResults.Ok(new CareerResponse(rows.Select(r => new CareerRowDto(r.Year,
            Length(r.Nine), Length(r.Eighteen), r.Holes, r.ToPar, AverageDto.From(r.PuttsPerHole),
            RatioDto.From(r.Gir), RatioDto.From(r.DrivingAccuracy), r.Earliest, r.Latest)).ToList()));
    }

    private static async Task<Ok<CoursesResponse>> GetCourses(
        int? year, HttpContext http, RoundDataLoader loader, CancellationToken ct)
    {
        IReadOnlyList<RoundData> rounds = await loader.LoadAsync(http.User.GetAccountId(), ct);
        if (year is { } y)
        {
            rounds = SeasonStats.ForYear(rounds, y);
        }

        return TypedResults.Ok(new CoursesResponse(CareerStats.ByCourseAndTee(rounds).Select(r => new CourseRowDto(
            r.CourseId, r.CourseName, r.TeeId, r.TeeName, Length(r.Nine), Length(r.Eighteen), r.Holes, r.ToPar,
            AverageDto.From(r.PuttsPerHole), RatioDto.From(r.Gir), RatioDto.From(r.DrivingAccuracy),
            AverageDto.From(r.Par3ToPar), AverageDto.From(r.Par4ToPar), AverageDto.From(r.Par5ToPar),
            r.Earliest, r.Latest)).ToList()));
    }

    private static async Task<Ok<HandicapHistoryResponse>> GetHandicap(
        HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var accountId = http.User.GetAccountId();
        var entries = await db.HandicapEntries
            .AsNoTracking()
            .Where(h => h.AccountId == accountId)
            .OrderBy(h => h.EffectiveOn)
            .Select(h => new HandicapPointDto(h.EffectiveOn, h.Index))
            .ToListAsync(ct);
        return TypedResults.Ok(new HandicapHistoryResponse(entries));
    }

    private static async Task<Results<Ok<ScorecardResponse>, NotFound>> GetScorecard(
        Guid id, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var accountId = http.User.GetAccountId();
        var round = await db.Rounds
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.HoleResults)
            .Include(r => r.TeeVersion).ThenInclude(v => v.Holes)
            .Include(r => r.TeeVersion).ThenInclude(v => v.Tee).ThenInclude(t => t.Course)
            .SingleOrDefaultAsync(r => r.Id == id && r.AccountId == accountId && r.DeletedAt == null, ct);
        if (round is null)
        {
            return TypedResults.NotFound();
        }

        var version = round.TeeVersion;
        var results = round.HoleResults.ToDictionary(h => h.HoleNumber);
        var holes = version.Holes.OrderBy(h => h.Number).Select(h => new ScorecardHoleDto(
            h.Number, h.Par, h.Yardage, h.StrokeIndex,
            results.TryGetValue(h.Number, out var r)
                ? new ScorecardHoleResultDto(r.EntrySequence, r.Score, r.Putts, r.FairwayHit, r.TeeShotLeft,
                    r.TeeShotRight, r.TeeShotLong, r.TeeShotShort, r.Gir, r.PenaltyStrokes,
                    r.GreensideSandShots, r.FairwaySandShots)
                : null)).ToList();

        var played = holes.Where(h => h.Result is not null).ToList();
        var score = played.Sum(h => h.Result!.Score);
        return TypedResults.Ok(new ScorecardResponse(round.Id, round.PlayedOn,
            version.Tee.CourseId, version.Tee.Course.Name, version.Tee.Id, version.Tee.Name,
            version.EffectiveFrom, version.Rating, version.Slope, version.Par,
            played.Count, score, score - played.Sum(h => h.Par), played.Sum(h => h.Result!.Putts), holes));
    }

    private static LengthSummaryDto Length(LengthSummary l) =>
        new(l.Rounds, AverageDto.From(l.Average), l.Low, l.High);
}
