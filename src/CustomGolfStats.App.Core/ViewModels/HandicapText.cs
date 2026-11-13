using System.Globalization;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>
/// How a handicap index is written. A plus handicap (better than scratch) is stored as a negative number, as the API and the web
/// app do, and is shown and typed with a leading "+": an index of -2.1 is "+2.1". A bare number is a normal index. A leading "-" is
/// refused as ambiguous.
/// </summary>
public static class HandicapText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(double? index) => index switch
    {
        null => UiText.NoValue,
        < 0 => "+" + (-index.Value).ToString("0.0", Inv),
        { } i => i.ToString("0.0", Inv),
    };

    /// <summary>Reads typed text ("," or "." as the decimal separator). False for empty, unreadable or "-"-prefixed text.</summary>
    public static bool TryParse(string? text, out double index)
    {
        index = 0;
        var t = (text ?? "").Trim().Replace(',', '.');
        var plus = t.StartsWith('+');
        if (plus)
            t = t[1..].TrimStart();
        if (t.Length == 0 || t.StartsWith('-') || t.StartsWith('+'))
            return false;
        if (!double.TryParse(t, NumberStyles.AllowDecimalPoint, Inv, out var value))
            return false;
        index = plus && value != 0 ? -value : value;
        return true;
    }
}
