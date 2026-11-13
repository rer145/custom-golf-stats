using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Migration.Import;
using CustomGolfStats.Migration.Mapping;
using CustomGolfStats.Migration.Report;

namespace CustomGolfStats.Migration.Tests.Report;

public sealed class MigrationReportTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2024, 6, 1);

    private static ImportResult Result(IReadOnlyList<Round> rounds, IReadOnlyList<MigrationFinding> findings, bool committed = true) =>
        new(Guid.Empty, 42, committed, new ImportCounts(2, 1, 3, 45, rounds.Count, rounds.Sum(r => r.HoleResults.Count), 2, 0, 0, 0),
            new MappedImport([], rounds, [], findings));

    private static Round Round(DateOnly playedOn, int holes) =>
        new()
        {
            PlayedOn = playedOn,
            HoleResults = Enumerable.Range(1, holes).Select(n => new HoleResult { HoleNumber = n, EntrySequence = n }).ToList(),
        };

    [Fact]
    public void Lists_counts_per_year_and_every_kind_of_finding()
    {
        var rounds = new[] { Round(new DateOnly(2023, 5, 1), 18), Round(Day, 18), Round(Day, 9), Round(Day, 12) };
        MigrationFinding[] findings =
        [
            new TeeRatingDefaulted(5, "Pine | Valley", "White", "Rating", null, "0"),
            new TeeSkipped(6, "Bare", "Black", "no holes"),
            new DuplicateTeeHoleNumber(7, 4, 70, [70, 71]),
            new HoleOutOfRange(8, "Wild", "Long", 80, 19, "Number", 19),
            new RoundTeeReplaced(19, Day, 2192, "Course #1 - 2022", 1171, "Course #1 - 2020"),
            new RoundSkipped(9, Day, "no active hole details"),
            new UnknownRoundType(10, Day, 3, "Scramble"),
            new RoundValueDropped(11, Day, "Duration", "about four hours"),
            new EntrySequenceFallback(12, Day, [1, 2]),
            new DuplicateRounds(Day, 5, [13, 14, 15]),
            new ZeroPuttHole(16, 160, Day, 7, 4, 4, true, false),
            new ZeroPuttHole(17, 170, Day, 3, 4, 3, false, true),
            new ZeroPuttHole(18, 180, new DateOnly(2023, 5, 1), 2, 5, 3, true, true),
        ];

        var markdown = MigrationReport.ToMarkdown(Result(rounds, findings), "ann@example.test", GeneratedAt);

        Assert.Contains("golfer 42", markdown, StringComparison.Ordinal);
        Assert.Contains("ann@example.test", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2024 | 3 | 1 | 1 | 1 | 39 | 2 | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2023 | 1 | 0 | 1 | 0 | 18 | 1 | 0 |", markdown, StringComparison.Ordinal);
        Assert.Contains("Pine \\| Valley", markdown, StringComparison.Ordinal);
        foreach (var heading in new[]
                 {
                     "## Summary", "## Rounds per year", "## Skipped rounds", "## Skipped tees", "## Duplicate rounds",
                     "## Holes with zero putts", "## Defaulted ratings", "## Unknown round types", "## Dropped values",
                     "## Entry sequence fallbacks", "## Duplicate hole rows", "## Holes outside the API's ranges",
                     "## Rounds moved to the tee of their holes",
                 })
        {
            Assert.Contains(heading, markdown, StringComparison.Ordinal);
        }

        Assert.Contains("13, 14, 15", markdown, StringComparison.Ordinal);
        Assert.Contains("about four hours", markdown, StringComparison.Ordinal);
        Assert.Contains("| Course #1 - 2022 (2192) | Course #1 - 2020 (1171) |", markdown, StringComparison.Ordinal);
        Assert.Contains("number 1-18, par 3-6 and yardage 0-1000", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("None.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_sections_say_none_and_a_dry_run_says_so()
    {
        var markdown = MigrationReport.ToMarkdown(Result([], [], committed: false), "ann@example.test", GeneratedAt);

        Assert.Contains("Dry run", markdown, StringComparison.Ordinal);
        Assert.Contains("None.", markdown, StringComparison.Ordinal);
    }
}
