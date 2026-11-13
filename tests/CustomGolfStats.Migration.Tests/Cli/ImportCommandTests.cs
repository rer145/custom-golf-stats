using CustomGolfStats.Migration.Cli;
using CustomGolfStats.Migration.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomGolfStats.Migration.Tests.Cli;

public sealed class ImportCommandTests(MsSqlFixture sql, PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>Recognisable, so a leak anywhere in the output is easy to spot.</summary>
    private const string FakePassword = "Leak-Canary-9f2c7e41-Pw!";

    private MigrationScenario _scenario = null!;
    private string _reportPath = null!;

    public async ValueTask InitializeAsync()
    {
        _scenario = await MigrationScenario.CreateAsync(sql, postgres);
        _reportPath = Path.Combine(Path.GetTempPath(), $"cgs-migration-{Guid.NewGuid():N}", "report.md");
    }

    public ValueTask DisposeAsync()
    {
        var directory = Path.GetDirectoryName(_reportPath)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(
        IReadOnlyDictionary<string, string?> environment, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await MigrationCli.RunAsync(
            args, output, error, name => environment.GetValueOrDefault(name), TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private Dictionary<string, string?> Environment(string? source = null, string? target = null) => new()
    {
        ["GOLFTRACK_SOURCE"] = source ?? _scenario.SourceConnectionString,
        ["CGS_TARGET"] = target ?? _scenario.TargetConnectionString,
    };

    private string[] ImportArgs(params string[] extra) =>
    [
        "import", "--golfer-id", _scenario.GolferId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--account-email", MigrationScenario.AccountEmail, "--report", _reportPath, .. extra,
    ];

    [Fact]
    public async Task Import_writes_the_rows_and_the_report_without_leaking_connection_strings()
    {
        var (exitCode, output, error) = await RunAsync(Environment(), ImportArgs());

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Contains("4 rounds", output, StringComparison.Ordinal);
        Assert.Contains(_reportPath, output, StringComparison.Ordinal);
        var report = await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken);
        Assert.Contains("## Rounds per year", report, StringComparison.Ordinal);
        Assert.Contains("Scramble", report, StringComparison.Ordinal);
        Assert.Contains("## Duplicate rounds", report, StringComparison.Ordinal);

        var sourcePassword = new SqlConnectionStringBuilder(_scenario.SourceConnectionString).Password;
        Assert.False(string.IsNullOrEmpty(sourcePassword));
        foreach (var secret in new[] { sourcePassword, _scenario.SourceConnectionString, _scenario.TargetConnectionString })
        {
            Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, report, StringComparison.Ordinal);
        }

        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(4, await db.Rounds.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Options_take_the_connection_strings_when_the_environment_has_none()
    {
        var (exitCode, output, error) = await RunAsync(
            new Dictionary<string, string?>(),
            ImportArgs("--source", _scenario.SourceConnectionString, "--target", _scenario.TargetConnectionString, "--dry-run"));

        Assert.Equal(0, exitCode);
        Assert.Equal("", error);
        Assert.Contains("Dry run", output, StringComparison.Ordinal);
        Assert.DoesNotContain(_scenario.SourceConnectionString, output, StringComparison.Ordinal);
        Assert.Contains("Dry run", await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(0, await db.Rounds.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Wrong_target_password_fails_without_showing_it()
    {
        var target = new NpgsqlConnectionStringBuilder(_scenario.TargetConnectionString) { Password = FakePassword }.ConnectionString;

        var (exitCode, output, error) = await RunAsync(Environment(target: target), ImportArgs());

        Assert.Equal(1, exitCode);
        Assert.NotEqual("", error);
        Assert.DoesNotContain(FakePassword, output + error, StringComparison.Ordinal);
        Assert.DoesNotContain(target, output + error, StringComparison.Ordinal);
        Assert.False(File.Exists(_reportPath));
    }

    [Fact]
    public async Task Wrong_source_password_fails_without_showing_it()
    {
        var source = new SqlConnectionStringBuilder(_scenario.SourceConnectionString) { Password = FakePassword }.ConnectionString;

        var (exitCode, output, error) = await RunAsync(Environment(source: source), ImportArgs());

        Assert.Equal(1, exitCode);
        Assert.NotEqual("", error);
        Assert.DoesNotContain(FakePassword, output + error, StringComparison.Ordinal);
        Assert.DoesNotContain(source, output + error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_failure_after_commit_says_the_import_was_committed()
    {
        Directory.CreateDirectory(_reportPath); // a directory where the report file should go: writing it fails

        var (exitCode, _, error) = await RunAsync(Environment(), ImportArgs());

        Assert.Equal(1, exitCode);
        Assert.Contains("WAS committed", error, StringComparison.Ordinal);
        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(4, await db.Rounds.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_report_path_exits_2_before_importing()
    {
        var (exitCode, _, error) = await RunAsync(
            Environment(),
            "import", "--golfer-id", "1", "--account-email", MigrationScenario.AccountEmail, "--report", "bad\0path.md");

        Assert.Equal(2, exitCode);
        Assert.Contains("Can't write the report", error, StringComparison.Ordinal);
        await using var db = _scenario.CreateTargetContext();
        Assert.Equal(0, await db.Rounds.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Importing_another_golfer_into_the_account_warns_about_the_rounds_it_clears()
    {
        Assert.Equal(0, (await RunAsync(Environment(), ImportArgs())).ExitCode);

        var (exitCode, _, error) = await RunAsync(
            Environment(),
            "import", "--golfer-id", _scenario.OtherGolferId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--account-email", MigrationScenario.AccountEmail, "--report", _reportPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Warning: cleared 4 previously imported rounds", error, StringComparison.Ordinal);
        Assert.Contains("... of which from other golftrack golfers | 4 |", await File.ReadAllTextAsync(_reportPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_account_exits_2_with_a_clear_message()
    {
        var (exitCode, _, error) = await RunAsync(
            Environment(),
            "import", "--golfer-id", "1", "--account-email", "nobody@example.test", "--report", _reportPath);

        Assert.Equal(2, exitCode);
        Assert.Contains("nobody@example.test", error, StringComparison.Ordinal);
        Assert.False(File.Exists(_reportPath));
    }

    [Fact]
    public async Task Missing_arguments_exit_2()
    {
        var (exitCode, _, error) = await RunAsync(Environment(), "import", "--account-email", "a@example.test");

        Assert.Equal(2, exitCode);
        Assert.Contains("--golfer-id", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_connection_strings_exit_2_naming_the_variable()
    {
        var (exitCode, _, error) = await RunAsync(
            new Dictionary<string, string?> { ["CGS_TARGET"] = _scenario.TargetConnectionString }, ImportArgs());

        Assert.Equal(2, exitCode);
        Assert.Contains("GOLFTRACK_SOURCE", error, StringComparison.Ordinal);
    }
}
