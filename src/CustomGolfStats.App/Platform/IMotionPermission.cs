namespace CustomGolfStats.App.Platform;

/// <summary>Gets the pedometer ready for a round: on Android this asks for the activity-recognition permission and starts sampling
/// the step counter (Android only counts steps while someone is listening); on iOS the motion coprocessor keeps history, so it is a
/// no-op there. Called when a round screen opens.</summary>
public interface IMotionPermission
{
    Task PrepareAsync();
}

public sealed class NoMotionPreparation : IMotionPermission
{
    public Task PrepareAsync() => Task.CompletedTask;
}
