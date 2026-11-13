using System.CommandLine;
using System.Globalization;
using CustomGolfStats.Api.Data;
using CustomGolfStats.Migration.Import;
using CustomGolfStats.Migration.Report;
using CustomGolfStats.Migration.Source;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Migration.Cli;

/// <summary>
/// The command line. Exit codes: 0 success, 2 something the user can fix (arguments, connection settings, account),
/// 1 anything unexpected. Connection strings come from <c>--source</c>/<c>--target</c> or the
/// <see cref="SourceVariable"/>/<see cref="TargetVariable"/> environment variables and are never printed: every
/// message goes through <see cref="SecretRedactor"/>, and nothing is logged.
/// </summary>
public static class MigrationCli
{
    public const string SourceVariable = "GOLFTRACK_SOURCE";
    public const string TargetVariable = "CGS_TARGET";
    public const string DefaultReportPath = "golftrack-import-report.md";

    public const int Success = 0;
    public const int Failure = 1;
    public const int UserError = 2;

    public static async Task<int> RunAsync(
        string[] args, TextWriter output, TextWriter error, Func<string, string?> environment, CancellationToken cancellationToken = default)
    {
        var sourceOption = new Option<string?>("--source")
        {
            Description = $"golftrack SQL Server connection string (default: the {SourceVariable} environment variable).",
        };
        var targetOption = new Option<string?>("--target")
        {
            Description = $"Custom Golf Stats Postgres connection string (default: the {TargetVariable} environment variable).",
        };
        var golferOption = new Option<int>("--golfer-id") { Description = "golftrack Golfer.Id to import.", Required = true };
        var emailOption = new Option<string>("--account-email")
        {
            Description = "Email of the existing Custom Golf Stats account to import into.",
            Required = true,
        };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Do everything, then roll back instead of committing." };
        var reportOption = new Option<string?>("--report") { Description = $"Markdown report path (default: {DefaultReportPath})." };

        var import = new Command("import", "Import one golftrack golfer's rounds, courses and handicap history into an account.")
        {
            golferOption, emailOption, dryRunOption, reportOption, sourceOption, targetOption,
        };
        var root = new RootCommand("Migrates golftrack data into Custom Golf Stats.") { import };

        // No @response files: an argument is only ever what was typed.
        var parse = root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
        var redactor = new SecretRedactor(
            [environment(SourceVariable), environment(TargetVariable), parse.GetValue(sourceOption), parse.GetValue(targetOption)]);
        if (parse.Errors.Count > 0)
        {
            foreach (var parseError in parse.Errors)
            {
                await error.WriteLineAsync(redactor.Redact(parseError.Message));
            }

            return UserError;
        }

        import.SetAction(async (result, token) =>
        {
            var source = Setting(result.GetValue(sourceOption), environment(SourceVariable));
            var target = Setting(result.GetValue(targetOption), environment(TargetVariable));
            if (source is null || target is null)
            {
                await error.WriteLineAsync(source is null
                    ? $"No golftrack database: set {SourceVariable} or pass --source."
                    : $"No Custom Golf Stats database: set {TargetVariable} or pass --target.");
                return UserError;
            }

            return await ImportAsync(
                source, target, result.GetValue(golferOption), result.GetValue(emailOption)!, result.GetValue(dryRunOption),
                Path.GetFullPath(result.GetValue(reportOption) ?? DefaultReportPath), output, error, redactor, token);
        });

        return await parse.InvokeAsync(
            new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false }, cancellationToken);
    }

    private static string? Setting(string? option, string? variable) =>
        !string.IsNullOrWhiteSpace(option) ? option : !string.IsNullOrWhiteSpace(variable) ? variable : null;

    private static async Task<int> ImportAsync(
        string source,
        string target,
        int golferId,
        string accountEmail,
        bool dryRun,
        string reportPath,
        TextWriter output,
        TextWriter error,
        SecretRedactor redactor,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(target).Options;
            await using var db = new AppDbContext(options, TimeProvider.System);
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                throw new MigrationUserException(
                    "The Custom Golf Stats database is not up to date. Run the API (it applies its migrations) and try again.");
            }

            var importer = new Importer(new GolftrackReader(source), db, TimeProvider.System);
            var result = await importer.RunAsync(golferId, accountEmail, dryRun, cancellationToken);

            var markdown = MigrationReport.ToMarkdown(result, accountEmail, TimeProvider.System.GetUtcNow());
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, markdown, cancellationToken);

            var c = result.Counts;
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"""
                {(result.Committed ? "Imported" : "Dry run: nothing was committed. Would import")} golftrack golfer {golferId} into {accountEmail.Trim()}:
                  {c.Rounds} rounds ({c.HoleResults} hole results), {c.HandicapEntries} handicap entries,
                  {c.CoursesWritten} courses written, {c.CoursesReused} reused, {c.TeesWritten} tees, {c.TeeHolesWritten} tee holes.
                  Cleared from a previous import: {c.RoundsRemoved} rounds, {c.HandicapEntriesRemoved} handicap entries, {c.CoursesRemoved} courses.
                  {result.Mapped.Findings.Count} findings.
                Report: {reportPath}
                """));
            return Success;
        }
        catch (MigrationUserException e)
        {
            await error.WriteLineAsync(redactor.Redact(e.Message));
            return UserError;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Cancelled. Nothing was committed.");
            return Failure;
        }
#pragma warning disable CA1031 // Any failure ends the run with exit code 1 and a redacted message (never a stack trace).
        catch (Exception e)
#pragma warning restore CA1031
        {
            await error.WriteLineAsync("Import failed: " + redactor.Redact(SecretRedactor.Describe(e)));
            return Failure;
        }
    }
}
