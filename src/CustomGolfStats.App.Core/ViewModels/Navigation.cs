namespace CustomGolfStats.App.Core.ViewModels;

public enum Route
{
    SignIn, Home, CoursePicker, ManualCourse, StartRound, Round, QuickHole, ShotEntry,
    RoundSummary, Scorecard, OverallStats, Profile,
}

/// <summary>Implemented by the Uno app. The parameter is one of the records below (or null).</summary>
public interface INavigator
{
    Task NavigateAsync(Route route, object? parameter = null);
    Task GoBackAsync();
}

public sealed record CourseArgs(Guid CourseId);

public sealed record RoundArgs(Guid RoundId);

/// <summary>Open a hole of a round: an existing playing by <paramref name="EntrySequence"/>, or a new one for
/// <paramref name="HoleNumber"/> (null means the suggested next hole).</summary>
public sealed record HoleArgs(Guid RoundId, int? EntrySequence = null, int? HoleNumber = null);
