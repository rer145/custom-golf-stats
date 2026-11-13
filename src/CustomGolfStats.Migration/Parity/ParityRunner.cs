using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Features.Stats;
using CustomGolfStats.Migration.Import;
using CustomGolfStats.Migration.Mapping;
using CustomGolfStats.Migration.Report;
using CustomGolfStats.Migration.Source;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomGolfStats.Migration.Parity;

/// <summary>
/// For every year in which the golfer has golftrack rounds or imported rounds, runs golftrack's statistics procedures
/// and compares them with the domain stats over the imported rounds (loaded the way the API's stats endpoints load
/// them). Only rounds imported from this golftrack golfer are used; rounds entered in the app are left out. Reads only.
/// </summary>
public sealed class ParityRunner(GolftrackReader source, LegacyStatsReader legacyStats, AppDbContext target, TimeProvider time)
{
    public async Task<ParityResult> RunAsync(int golferId, string accountEmail, CancellationToken cancellationToken = default)
    {
        var accountId = await AccountLookup.FindAsync(target, accountEmail, cancellationToken);
        var legacy = await source.ReadAsync(golferId, cancellationToken);
        var legacyRounds = legacy.Rounds.ToDictionary(r => DeterministicIds.For("Round", r.Id));
        var legacyByImportedId = new ParityLegacyRounds(legacyRounds, legacy.Tees.ToDictionary(t => t.Id));
        var imported = (await new RoundDataLoader(target, NullLogger<RoundDataLoader>.Instance).LoadAsync(accountId, cancellationToken))
            .Where(r => legacyRounds.ContainsKey(r.Id))
            .ToList();
        if (imported.Count == 0)
        {
            throw new MigrationUserException(
                $"No rounds of golftrack golfer {golferId} are imported into {accountEmail.Trim()}. Run import first.");
        }

        var findings = GolftrackMapper.Map(legacy, accountId, time.GetUtcNow()).Findings;
        var facts = (await legacyStats.ReadYearFactsAsync(golferId, cancellationToken)).ToDictionary(f => f.Year);
        var career = await legacyStats.RunAsync("Stats_CareerStats", new Dictionary<string, int> { ["GolferId"] = golferId }, cancellationToken);

        var years = facts.Keys.Concat(imported.Select(r => r.PlayedOn.Year)).Distinct().Order().ToList();
        var results = new List<ParityYear>();
        foreach (var year in years)
        {
            var byYear = new Dictionary<string, int> { ["year"] = year };
            var legacyYear = new LegacyYearResults(
                await legacyStats.RunAsync("Stats_ScoringStats", byYear, cancellationToken),
                await legacyStats.RunAsync("Stats_DrivingStats", byYear, cancellationToken),
                await legacyStats.RunAsync("Stats_ApproachStats", byYear, cancellationToken),
                await legacyStats.RunAsync("Stats_PuttingStats", byYear, cancellationToken),
                await legacyStats.RunAsync("Round_SeasonSummary", byYear, cancellationToken),
                career,
                await legacyStats.RunAsync(
                    "Stats_CourseStats", new Dictionary<string, int> { ["year"] = year, ["GolferId"] = golferId }, cancellationToken));

            var rounds = imported.Where(r => r.PlayedOn.Year == year).ToList();
            var fact = facts.GetValueOrDefault(year);
            var context = new ParityContext(
                fact?.GolferRounds ?? 0,
                rounds.Count,
                fact?.OtherGolfersRounds ?? 0,
                fact?.InactiveDetails ?? 0,
                fact?.StoredScoreDiffers ?? 0,
                fact?.HolesPlayedDiffers ?? 0,
                findings.OfType<RoundSkipped>().Count(f => f.PlayedOn.Year == year),
                findings.OfType<RoundTeeReplaced>().Count(f => f.PlayedOn.Year == year));
            results.Add(new ParityYear(
                year, context, ParityComparer.Compare(year, rounds, legacyByImportedId, legacyYear, context.OtherGolfersRounds)));
        }

        return new ParityResult(golferId, accountId, results);
    }
}
