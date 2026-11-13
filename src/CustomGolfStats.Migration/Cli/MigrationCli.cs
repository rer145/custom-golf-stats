using System.CommandLine;
using System.Globalization;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Migration.Import;
using CustomGolfStats.Migration.Parity;
using CustomGolfStats.Migration.Report;
using CustomGolfStats.Migration.Source;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Migration.Cli;

/// <summary>
/// The command line: <c>import</c> and <c>parity</c>. Exit codes: 0 success, 2 something the user can fix (arguments,
/// connection settings, report path, account, nothing imported), 1 anything unexpected. Connection strings come from
/// <c>--source</c>/<c>--target</c> or the <see cref="SourceVariable"/>/<see cref="TargetVariable"/> environment
/// variables and are never printed: every message goes through <see cref="SecretRedactor"/>, and nothing is logged.
/// </summary>
public static class MigrationCli
{
    public const string SourceVariable = "GOLFTRACK_SOURCE";
    public const string TargetVariable = "CGS_TARGET";
    public const string DefaultReportPath = "golftrack-import-report.md";
    public const string DefaultParityReportPath = "golftrack-parity-report.md";

    public const int Success = 0;
    public const int Failure = 1;
    public const int UserError = 2;

    public static async Task<int> RunAsync(
        string[] args, TextWriter output, TextWriter error, Func<string, string?> environment, CancellationToken cancellationToken = default)
    {
        var redactor = new SecretRedactor([environment(SourceVariable), environment(TargetVariable), .. ConnectionArguments(args)]);
        var settings = new Settings(output, error, environment, redactor);
        var root = new RootCommand("Migrates golftrack data into Custom Golf Stats.") { ImportCommand(settings), ParityCommand(settings) };

        // No @response files: an argument is only ever what was typed.
        var parse = root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
        if (parse.Errors.Count > 0)
        {
            foreach (var parseError in parse.Errors)
            {
                await error.WriteLineAsync(redactor.Redact(parseError.Message));
            }

            return UserError;
        }

        return await parse.InvokeAsync(
            new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false }, cancellationToken);
    }

    private static Command ImportCommand(Settings settings)
    {
        var common = new CommonOptions("Email of the existing Custom Golf Stats account to import into.", DefaultReportPath);
        var dryRun = new Option<bool>("--dry-run") { Description = "Do everything, then roll back instead of committing." };
        var command = new Command("import", "Import one golftrack golfer's rounds, courses and handicap history into an account.");
        common.AddTo(command);
        command.Options.Add(dryRun);
        command.SetAction((result, token) => RunAsync(settings, common, result, new Messages("Failed, nothing was committed: ", "Cancelled. Nothing was committed."), async (source, db, reportPath, state, ct) =>
        {
            var golferId = result.GetValue(common.Golfer);
            var email = result.GetValue(common.Email)!;
            var import = await new Importer(new GolftrackReader(source), db, TimeProvider.System)
                .RunAsync(golferId, email, result.GetValue(dryRun), ct);
            state.Committed = import.Committed;

            var c = import.Counts;
            if (c.RoundsRemovedFromOtherGolfers > 0)
            {
                await settings.Error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"Warning: cleared {c.RoundsRemovedFromOtherGolfers} previously imported rounds that came from a different golftrack golfer than {golferId}."));
            }

            await File.WriteAllTextAsync(reportPath, MigrationReport.ToMarkdown(import, email, TimeProvider.System.GetUtcNow()), CancellationToken.None);
            await settings.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"""
                {(import.Committed ? "Imported" : "Dry run: nothing was committed. Would import")} golftrack golfer {golferId} into {email.Trim()}:
                  {c.Rounds} rounds ({c.HoleResults} hole results), {c.HandicapEntries} handicap entries,
                  {c.CoursesWritten} courses written, {c.CoursesReused} reused, {c.TeesWritten} tees, {c.TeeHolesWritten} tee holes.
                  Cleared from a previous import: {c.RoundsRemoved} rounds, {c.HandicapEntriesRemoved} handicap entries, {c.CoursesRemoved} courses.
                  {import.Mapped.Findings.Count} findings.
                Report: {reportPath}
                """));
        }, token));
        return command;
    }

    private static Command ParityCommand(Settings settings)
    {
        var common = new CommonOptions("Email of the Custom Golf Stats account the golfer was imported into.", DefaultParityReportPath);
        var command = new Command("parity", "Compare golftrack's statistics with the new stats over the imported rounds, year by year.");
        common.AddTo(command);
        command.SetAction((result, token) => RunAsync(settings, common, result, new Messages("Parity failed: ", "Cancelled."), async (source, db, reportPath, _, ct) =>
        {
            var golferId = result.GetValue(common.Golfer);
            var email = result.GetValue(common.Email)!;
            var parity = await new ParityRunner(new GolftrackReader(source), new LegacyStatsReader(source), db, TimeProvider.System)
                .RunAsync(golferId, email, ct);
            await File.WriteAllTextAsync(reportPath, ParityReport.ToMarkdown(parity, email, TimeProvider.System.GetUtcNow()), ct);

            var rows = parity.Years.SelectMany(y => y.Sections).SelectMany(s => s.Rows).ToList();
            await settings.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"""
                Parity for golftrack golfer {golferId}, {parity.Years.Count} years:
                  {rows.Count(r => r.Verdict == Verdict.Match)} match, {rows.Count(r => r.Verdict == Verdict.Intended)} intended, {rows.Count(r => r.Verdict == Verdict.Unexplained)} unexplained, {rows.Count(r => r.Verdict == Verdict.NotCompared)} not compared.
                Report: {reportPath}
                """));
        }, token));
        return command;
    }

    /// <summary>
    /// Shared by both commands: resolves the connection strings and report path (exit 2 when unusable), opens the target
    /// (exit 2 when it isn't migrated) and turns failures into redacted messages. <paramref name="work"/> sets
    /// <see cref="RunState.Committed"/> as soon as it has committed, so a later failure or cancellation says so.
    /// </summary>
    private static async Task<int> RunAsync(
        Settings settings,
        CommonOptions common,
        ParseResult result,
        Messages messages,
        Func<string, AppDbContext, string, RunState, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        var (error, redactor) = (settings.Error, settings.Redactor);
        var source = Setting(result.GetValue(common.Source), settings.Environment(SourceVariable));
        var target = Setting(result.GetValue(common.Target), settings.Environment(TargetVariable));
        if (source is null || target is null)
        {
            await error.WriteLineAsync(source is null
                ? $"No golftrack database: set {SourceVariable} or pass --source."
                : $"No Custom Golf Stats database: set {TargetVariable} or pass --target.");
            return UserError;
        }

        var reportOption = result.GetValue(common.Report) ?? common.DefaultReport;
        string reportPath;
        try
        {
            // Before any work, so a bad path costs nothing.
            reportPath = Path.GetFullPath(reportOption);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await error.WriteLineAsync(redactor.Redact($"Can't write the report to {reportOption}: {e.Message}"));
            return UserError;
        }

        var state = new RunState();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(target).Options;
            await using var db = new AppDbContext(options, TimeProvider.System);
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                throw new MigrationUserException(
                    "The Custom Golf Stats database is not up to date. Run the API (it applies its migrations) and try again.");
            }

            await work(source, db, reportPath, state, cancellationToken);
            return Success;
        }
        catch (MigrationUserException e)
        {
            await error.WriteLineAsync(redactor.Redact(e.Message));
            return UserError;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync(state.Committed
                ? "Cancelled after the import was committed: the data is in the target, but the report may be missing."
                : messages.Cancelled);
            return Failure;
        }
#pragma warning disable CA1031 // Any failure ends the run with exit code 1 and a redacted message (never a stack trace).
        catch (Exception e)
#pragma warning restore CA1031
        {
            var message = redactor.Redact(SecretRedactor.Describe(e));
            await error.WriteLineAsync(state.Committed
                ? "The import WAS committed, but writing the report failed: " + message
                : messages.FailurePrefix + message);
            return Failure;
        }
    }

    private static string? Setting(string? option, string? variable) =>
        !string.IsNullOrWhiteSpace(option) ? option : !string.IsNullOrWhiteSpace(variable) ? variable : null;

    /// <summary>Values given to --source/--target (both "--source value" and "--source=value"), so they can be masked.</summary>
    private static IEnumerable<string> ConnectionArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            foreach (var name in new[] { "--source", "--target" })
            {
                if (args[i] == name && i + 1 < args.Length)
                {
                    yield return args[i + 1];
                }
                else if (args[i].StartsWith(name + "=", StringComparison.Ordinal) || args[i].StartsWith(name + ":", StringComparison.Ordinal))
                {
                    yield return args[i][(name.Length + 1)..];
                }
            }
        }
    }

    /// <summary>What a command says when it fails or is cancelled before committing anything.</summary>
    private sealed record Messages(string FailurePrefix, string Cancelled);

    private sealed record Settings(TextWriter Output, TextWriter Error, Func<string, string?> Environment, SecretRedactor Redactor);

    private sealed class RunState
    {
        public bool Committed { get; set; }
    }

    private sealed class CommonOptions(string emailDescription, string defaultReport)
    {
        public Option<string?> Source { get; } = new("--source")
        {
            Description = $"golftrack SQL Server connection string (default: the {SourceVariable} environment variable).",
        };

        public Option<string?> Target { get; } = new("--target")
        {
            Description = $"Custom Golf Stats Postgres connection string (default: the {TargetVariable} environment variable).",
        };

        public Option<int> Golfer { get; } = new("--golfer-id") { Description = "golftrack Golfer.Id.", Required = true };

        public Option<string> Email { get; } = new("--account-email") { Description = emailDescription, Required = true };

        public Option<string?> Report { get; } = new("--report") { Description = $"Markdown report path (default: {defaultReport})." };

        public string DefaultReport { get; } = defaultReport;

        public void AddTo(Command command)
        {
            foreach (var option in new Option[] { Golfer, Email, Report, Source, Target })
            {
                command.Options.Add(option);
            }
        }
    }
}
