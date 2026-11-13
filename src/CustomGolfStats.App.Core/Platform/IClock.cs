namespace CustomGolfStats.App.Core.Platform;

/// <summary>The current time, injectable so dirty-flag and trial logic is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
