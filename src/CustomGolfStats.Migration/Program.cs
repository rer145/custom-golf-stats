using CustomGolfStats.Migration.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await MigrationCli.RunAsync(args, Console.Out, Console.Error, Environment.GetEnvironmentVariable, cancellation.Token);
