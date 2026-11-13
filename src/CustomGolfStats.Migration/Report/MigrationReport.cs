using System.Globalization;
using System.Text;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Migration.Import;

namespace CustomGolfStats.Migration.Report;

/// <summary>
/// The Markdown import report: what was written, rounds per year, and every finding (nothing is left out or
/// summarised away). Never contains a connection string: it is built only from the import result.
/// </summary>
public static class MigrationReport
{
    public static string ToMarkdown(ImportResult result, string accountEmail, DateTimeOffset generatedAt)
    {
        var findings = result.Mapped.Findings;
        var md = new MarkdownWriter();

        md.Line("# golftrack import report");
        md.Line();
        md.Line($"- Generated: {generatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC");
        md.Line($"- Source: golfer {result.GolferId}");
        md.Line($"- Target account: {MarkdownWriter.Escape(accountEmail.Trim())}");
        md.Line(result.Committed ? "- Mode: import (committed)" : "- Mode: **Dry run**: nothing was committed");
        md.Line();

        var c = result.Counts;
        md.Heading("Summary");
        md.Table(
            ["What", "Count"],
            [
                ["Courses written", Num(c.CoursesWritten)],
                ["Courses reused (in use elsewhere, left as they are)", Num(c.CoursesReused)],
                ["Tees written", Num(c.TeesWritten)],
                ["Tee holes written", Num(c.TeeHolesWritten)],
                ["Rounds", Num(c.Rounds)],
                ["Hole results", Num(c.HoleResults)],
                ["Handicap entries", Num(c.HandicapEntries)],
                ["Previously imported rounds cleared", Num(c.RoundsRemoved)],
                ["... of which from other golftrack golfers", Num(c.RoundsRemovedFromOtherGolfers)],
                ["Previously imported handicap entries cleared", Num(c.HandicapEntriesRemoved)],
                ["... of which handicap entries from other golftrack golfers", Num(c.HandicapEntriesRemovedFromOtherGolfers)],
                ["Unused golftrack courses cleared", Num(c.CoursesRemoved)],
                ["Findings", Num(findings.Count)],
            ]);

        var zeroPutts = findings.OfType<ZeroPuttHole>().ToList();
        md.Heading("Rounds per year");
        md.Table(
            ["Year", "Rounds", "9 holes", "18 holes", "Other lengths", "Holes", "Zero-putt holes (score > 1)", "Of which not hole-out-like"],
            result.Mapped.Rounds
                .GroupBy(r => r.PlayedOn.Year)
                .OrderBy(g => g.Key)
                .Select(g => new[]
                {
                    g.Key.ToString(CultureInfo.InvariantCulture),
                    Num(g.Count()),
                    Num(g.Count(r => r.HoleResults.Count == 9)),
                    Num(g.Count(r => r.HoleResults.Count == 18)),
                    Num(g.Count(r => r.HoleResults.Count is not (9 or 18))),
                    Num(g.Sum(r => r.HoleResults.Count)),
                    Num(zeroPutts.Count(z => z.Year == g.Key)),
                    Num(zeroPutts.Count(z => z.Year == g.Key && !z.LooksLikeHoleOut)),
                })
                .ToList());

        md.Heading("Skipped rounds");
        md.Paragraph("Not imported.");
        md.Table(["golftrack round", "Played on", "Reason"],
            findings.OfType<RoundSkipped>().Select(f => new[] { Num(f.LegacyRoundId), Date(f.PlayedOn), f.Reason }).ToList());

        md.Heading("Rounds moved to the tee of their holes");
        md.Paragraph("Every detail of these rounds points at holes of another tee of the same course; they were imported on that tee.");
        md.Table(["golftrack round", "Played on", "Round's tee", "Holes' tee (used)"],
            findings.OfType<RoundTeeReplaced>()
                .Select(f => new[] { Num(f.LegacyRoundId), Date(f.PlayedOn), $"{f.RoundTeeName} ({Num(f.RoundTeeId)})", $"{f.HolesTeeName} ({Num(f.HolesTeeId)})" })
                .ToList());

        md.Heading("Skipped tees");
        md.Table(["golftrack tee", "Course", "Tee", "Reason"],
            findings.OfType<TeeSkipped>().Select(f => new[] { Num(f.LegacyTeeId), f.CourseName, f.TeeName, f.Reason }).ToList());

        md.Heading("Duplicate rounds");
        md.Paragraph("Same date, tee and hole-by-hole scores. All of them were imported.");
        md.Table(["Played on", "golftrack tee", "golftrack rounds"],
            findings.OfType<DuplicateRounds>()
                .Select(f => new[] { Date(f.PlayedOn), Num(f.LegacyTeeId), string.Join(", ", f.LegacyRoundIds.Select(Num)) })
                .ToList());

        md.Heading("Holes with zero putts");
        md.Paragraph("Every imported hole with 0 putts and a score above 1. \"Hole-out-like\" means golftrack's GIR flag agrees with " +
                "holing out from off the green (GIR exactly when the score is at most par - 2); the others suggest putts " +
                "were not entered. Counts per year are in \"Rounds per year\".");
        md.Table(["Played on", "golftrack round", "golftrack detail", "Hole", "Par", "Score", "GIR", "Hole-out-like"],
            zeroPutts
                .Select(f => new[]
                {
                    Date(f.PlayedOn), Num(f.LegacyRoundId), Num(f.LegacyRoundDetailId), Num(f.HoleNumber), Num(f.Par),
                    Num(f.Score), YesNo(f.Gir), YesNo(f.LooksLikeHoleOut),
                })
                .ToList());

        md.Heading("Defaulted ratings");
        md.Paragraph("Missing or out-of-range ratings and slopes: the 18-hole rating becomes 0 and slope 113; front and back " +
                "nine values become empty.");
        md.Table(["golftrack tee", "Course", "Tee", "Field", "golftrack value", "Stored"],
            findings.OfType<TeeRatingDefaulted>()
                .Select(f => new[] { Num(f.LegacyTeeId), f.CourseName, f.TeeName, f.Field, f.OriginalValue ?? "(empty)", f.StoredValue ?? "(empty)" })
                .ToList());

        md.Heading("Unknown round types");
        md.Paragraph("Imported as Casual.");
        md.Table(["golftrack round", "Played on", "Round type id", "Name"],
            findings.OfType<UnknownRoundType>()
                .Select(f => new[] { Num(f.LegacyRoundId), Date(f.PlayedOn), Num(f.RoundTypeId), f.Name ?? "(no such round type)" })
                .ToList());

        md.Heading("Dropped values");
        md.Paragraph("Round values that could not be stored; the round was imported without them.");
        md.Table(["golftrack round", "Played on", "Field", "golftrack value"],
            findings.OfType<RoundValueDropped>().Select(f => new[] { Num(f.LegacyRoundId), Date(f.PlayedOn), f.Field, f.Value }).ToList());

        md.Heading("Entry sequence fallbacks");
        md.Paragraph("Rounds whose sequence numbers (or hole numbers) repeat; holes were numbered in golftrack entry order instead.");
        md.Table(["golftrack round", "Played on", "Repeated sequences"],
            findings.OfType<EntrySequenceFallback>()
                .Select(f => new[] { Num(f.LegacyRoundId), Date(f.PlayedOn), string.Join(", ", f.RepeatedSequences.Select(Num)) })
                .ToList());

        md.Heading("Duplicate hole rows");
        md.Paragraph("Tees with several different golftrack Hole rows for one hole number.");
        md.Table(["golftrack tee", "Hole", "Kept hole row", "All hole rows"],
            findings.OfType<DuplicateTeeHoleNumber>()
                .Select(f => new[] { Num(f.LegacyTeeId), Num(f.HoleNumber), Num(f.KeptLegacyHoleId), string.Join(", ", f.LegacyHoleIds.Select(Num)) })
                .ToList());

        md.Heading("Holes outside the API's ranges");
        md.Paragraph(
            $"Imported as golftrack has them (number 1-{Num(CourseRules.MaxHoles)}, par {Num(CourseRules.MinPar)}-{Num(CourseRules.MaxPar)} " +
            $"and yardage 0-{Num(CourseRules.MaxYardage)} are what the API accepts on edit).");
        md.Table(["golftrack tee", "Course", "Tee", "golftrack hole", "Hole", "Field", "Value"],
            findings.OfType<HoleOutOfRange>()
                .Select(f => new[] { Num(f.LegacyTeeId), f.CourseName, f.TeeName, Num(f.LegacyHoleId), Num(f.HoleNumber), f.Field, Num(f.Value) })
                .ToList());

        return md.ToString();
    }

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string YesNo(bool value) => value ? "yes" : "no";
}
