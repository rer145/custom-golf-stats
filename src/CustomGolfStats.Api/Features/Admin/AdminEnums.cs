namespace CustomGolfStats.Api.Features.Admin;

internal static class AdminEnums
{
    /// <summary>Parses an enum name, ignoring case. Numbers and undefined names are rejected.</summary>
    public static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text)
            && !char.IsDigit(text[0]) && text[0] is not ('-' or '+')
            && Enum.TryParse(text, ignoreCase: true, out result)
            && Enum.IsDefined(result);
    }

    public static string Names<TEnum>()
        where TEnum : struct, Enum => string.Join(", ", Enum.GetNames<TEnum>());
}
