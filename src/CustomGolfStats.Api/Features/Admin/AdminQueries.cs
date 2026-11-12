namespace CustomGolfStats.Api.Features.Admin;

internal static class AdminQueries
{
    /// <summary>Escapes LIKE wildcards; use with the escape character <c>\</c>.</summary>
    public static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
}
