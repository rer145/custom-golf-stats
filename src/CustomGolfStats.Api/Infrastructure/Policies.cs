namespace CustomGolfStats.Api.Infrastructure;

public static class Policies
{
    /// <summary>Requires the <see cref="Domain.Role.Admin"/> role claim. Apply to the whole admin route group.</summary>
    public const string Admin = "Admin";
}
