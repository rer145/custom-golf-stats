using System.Globalization;
using CustomGolfStats.Migration.Report;

namespace CustomGolfStats.Migration.Parity;

/// <summary>The Markdown parity report: every compared number, per year and section, with its verdict.</summary>
public static class ParityReport
{
    public static string ToMarkdown(ParityResult result, string accountEmail, DateTimeOffset generatedAt)
    {
        var md = new MarkdownWriter();
        md.Line("# golftrack parity report");
        md.Line();
        md.Line($"- Generated: {generatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC");
        md.Line($"- Source: golfer {result.GolferId}");
        md.Line($"- Target account: {MarkdownWriter.Escape(accountEmail.Trim())}");
        md.Line();
        md.Paragraph(
            "golftrack's numbers come from its own statistics procedures; the new numbers from the domain stats over the " +
            "imported rounds of this golfer. **match**: equal (counts, to-par and dates exactly; averages and percentages " +
            "within ±0.05, golftrack's rounding). **intended**: different, and golftrack's value is exactly what golftrack's " +
            "definition gives on the imported data, for one of the rules below. **unexplained**: any other difference. " +
            "**not compared**: golftrack columns with no counterpart, missing columns and failed procedures. Percentages are 0-100.");
        md.Paragraph(
            "Note: Stats_ScoringStats, Stats_DrivingStats, Stats_ApproachStats, Stats_PuttingStats and Round_SeasonSummary " +
            "take only a year and read every golfer's rounds; see \"other golfers' rounds\" in each year's context.");

        md.Heading("Intended differences");
        md.Table(["Rule", "Reason"], IntendedRules.All.Select(r => new[] { r.Name, r.Reason }).ToList());

        md.Heading("Summary");
        md.Table(
            ["Year", "Match", "Intended", "Unexplained", "Not compared"],
            result.Years
                .Select(y =>
                {
                    var rows = y.Sections.SelectMany(s => s.Rows).ToList();
                    return new[] { Num(y.Year) }
                        .Concat(Enum.GetValues<Verdict>().Select(v => Num(rows.Count(r => r.Verdict == v))))
                        .ToArray();
                })
                .ToList());

        foreach (var year in result.Years)
        {
            md.Heading(Num(year.Year));
            var c = year.Context;
            md.Line($"- golftrack active rounds of this golfer: {Num(c.GolftrackRounds)}; imported: {Num(c.ImportedRounds)}");
            md.Line($"- Other golfers' active rounds this year (in golftrack's year-only procedures): {Num(c.OtherGolfersRounds)}");
            md.Line($"- Inactive hole details inside active rounds (golftrack's procedures count them; the import does not): {Num(c.InactiveDetailsInActiveRounds)}");
            md.Line($"- Rounds whose stored Score differs from their active details: {Num(c.RoundsWhoseStoredScoreDiffers)}");
            md.Line($"- Rounds whose HolesPlayed differs from their active details: {Num(c.RoundsWhoseHolesPlayedDiffers)}");
            md.Line($"- Rounds skipped by the import: {Num(c.RoundsSkippedByImport)}; moved to the tee of their holes: {Num(c.RoundsMovedToTheirHolesTee)}");
            md.Line();

            foreach (var section in year.Sections)
            {
                md.Heading($"{section.Title} (`{section.Procedure}`)", 3);
                if (section.Note is not null)
                {
                    md.Paragraph(section.Note);
                }

                md.Table(
                    ["Stat", "golftrack", "new", "Difference", "Verdict"],
                    section.Rows.Select(r => new[]
                    {
                        r.Column, Format(r.Golftrack, r.Kind), Format(r.New, r.Kind), Difference(r), VerdictText(r),
                    }).ToList());
            }
        }

        return md.ToString();
    }

    private static string VerdictText(ParityRow row)
    {
        var text = row.Verdict switch
        {
            Verdict.Match => "match",
            Verdict.Intended => $"intended: {row.Rule!.Name}",
            Verdict.Unexplained => "**unexplained**",
            _ => "not compared",
        };
        return row.Note is null ? text : $"{text} ({row.Note})";
    }

    private static string Difference(ParityRow row)
    {
        if (row.Golftrack is not { } g || row.New is not { } n)
        {
            return "";
        }

        var d = n - g;
        return row.Kind switch
        {
            ValueKind.Date => d.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " days",
            ValueKind.Percent => d.ToString("+0.00;-0.00;0", CultureInfo.InvariantCulture) + " pts",
            ValueKind.Average => d.ToString("+0.00;-0.00;0", CultureInfo.InvariantCulture),
            _ => d.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture),
        };
    }

    private static string Format(double? value, ValueKind kind) => value switch
    {
        null => "—",
        { } v => kind switch
        {
            ValueKind.Date => DateOnly.FromDayNumber((int)v).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ValueKind.Percent => v.ToString("0.00", CultureInfo.InvariantCulture) + "%",
            ValueKind.Average => v.ToString("0.00", CultureInfo.InvariantCulture),
            ValueKind.ToPar => v == 0 ? "E" : v.ToString("+0;-0", CultureInfo.InvariantCulture),
            _ => v.ToString("0.##", CultureInfo.InvariantCulture),
        },
    };

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
