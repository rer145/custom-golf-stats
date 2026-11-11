namespace CustomGolfStats.Domain.Holes;

internal static class DrivingHole
{
    /// <summary>A hole with a fairway tee shot: par 4 and above.</summary>
    internal static bool IsDrivingHole(int par) => par >= 4;
}
