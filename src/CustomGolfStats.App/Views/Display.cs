using System.Globalization;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Stats;
using Microsoft.UI.Xaml.Data;
using ClientGender = CustomGolfStats.Api.Client.Gender;
using ClientRoundType = CustomGolfStats.Api.Client.RoundType;

namespace CustomGolfStats.App.Views;

/// <summary>Words and number formats for the screens (no golf rules: those stay in the domain and the view models).</summary>
public static class Display
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Text(object? value) => value switch
    {
        null => "",
        Lie l => Lie(l),
        Club c => Club(c),
        ShotType t => t switch { ShotType.Tee => "Tee shot", _ => t.ToString() },
        Direction d => Direction(d),
        Shape s => s.ToString(),
        ClientRoundType r => r switch
        {
            ClientRoundType.Casual => "Casual",
            ClientRoundType.Tournament => "Tournament",
            _ => Words(r.ToString()),
        },
        ClientGender g => g switch { ClientGender.Unspecified => "Any", _ => g.ToString() },
        Enum e => Words(e.ToString()),
        _ => value.ToString() ?? "",
    };

    public static string Lie(Lie lie) => lie switch
    {
        Domain.Lie.FairwaySand => "Fairway bunker",
        Domain.Lie.GreensideSand => "Greenside bunker",
        Domain.Lie.PenaltyArea => "Penalty area",
        Domain.Lie.OutOfBounds => "Out of bounds",
        Domain.Lie.Holed => "Holed",
        _ => lie.ToString(),
    };

    public static string Club(Club club)
    {
        var name = club.ToString();
        foreach (var (prefix, word) in new[] { ("Wood", "wood"), ("Hybrid", "hybrid"), ("Iron", "iron") })
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                return $"{name[prefix.Length..]} {word}";
        return Words(name);
    }

    public static string Direction(Direction direction) => direction switch
    {
        Domain.Direction.OnTarget => "On target",
        _ => Words(direction.ToString()),
    };

    /// <summary>"ShortLeft" becomes "Short left".</summary>
    public static string Words(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
            return pascal;
        var chars = new List<char>(pascal.Length + 4) { pascal[0] };
        for (var i = 1; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && !char.IsUpper(pascal[i - 1]))
            {
                chars.Add(' ');
                chars.Add(char.ToLowerInvariant(pascal[i]));
            }
            else
                chars.Add(pascal[i]);
        }
        return new string([.. chars]);
    }

    /// <summary>"Club · City, Region" with the missing parts left out.</summary>
    public static string Place(string? club, string? city, string? region)
    {
        var where = string.Join(", ", new[] { city, region }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.Join("  ·  ", new[] { club, where }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static string FavoriteGlyph(bool favorite) => favorite ? "\uE735" : "\uE734";
    public static string FavoriteName(bool favorite) => favorite ? "Remove from favorites" : "Add to favorites";
    public static string ImportText(bool alreadyImported) => alreadyImported ? "Use" : "Import";

    public static string Date(DateTime date) => date.ToString("ddd d MMM yyyy", Inv);
    public static string ShortDate(DateTime date) => date.ToString("d MMM", Inv);
    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", Inv);
    public static string DateOrNone(DateTime? date) => date is { } d ? d.ToString("d MMM yyyy", Inv) : "";

    public static string Number(int value) => value.ToString(Inv);
    public static string Number(double? value, string format = "0.0") => value is { } v ? v.ToString(format, Inv) : Core.ViewModels.UiText.NoValue;
    public static string Percent(Ratio ratio) => ratio.Value is { } v ? (v * 100).ToString("0", Inv) + "%" : Core.ViewModels.UiText.NoValue;
    public static string Holes(int count) => count == 1 ? "1 hole" : $"{count} holes";
    public static string Thru(int holes) => holes == 0 ? "NO HOLES YET" : $"THRU {holes}";
    public static string HoleTitle(int number) => $"Hole {number}";
    public static string HoleFacts(int par, int yardage, int strokeIndex) =>
        string.Join("  ·  ", new[] { $"Par {par}", yardage > 0 ? $"{yardage} yd" : null, strokeIndex > 0 ? $"SI {strokeIndex}" : null }.Where(s => s is not null));
    public static string YesNo(bool value) => value ? "Yes" : "No";
    public static string Mark(bool value) => value ? "●" : "–";
    public static string TeeLine(string tee, DateTime playedOn) => $"{tee} tees  ·  {Date(playedOn)}".ToUpperInvariant();
    public static string RoundLine(DateTime playedOn, int holes, bool finished) =>
        $"{ShortDate(playedOn)}  ·  {Holes(holes)}{(finished ? "" : "  ·  in progress")}";
    public static string Score(int score) => score.ToString(Inv);
    public static string SuggestedGir(bool suggested) => suggested ? "Suggested: green in regulation" : "Suggested: missed green";
    public static string Handicap(double? index) => index is { } i ? i.ToString("0.0", Inv) : Core.ViewModels.UiText.NoValue;

    public static string AddHoleText(int? suggested) => suggested is { } n ? $"+ Add hole {n}" : "+ Add a hole";
    public static string ShotButtonText(int? editingIndex) => editingIndex is null ? "+ Add shot" : "Save shot changes";
    public static string DistanceHeader(string unit) => $"DISTANCE ({unit.ToUpperInvariant()})";

    /// <summary>One line describing a recorded shot, e.g. "Tee 412 yd · Driver → Fairway 145 yd · Left".</summary>
    public static string ShotLine(Domain.Shots.Shot s, string startUnit, string? endUnit)
    {
        var end = s.EndLie == Domain.Lie.Holed ? "Holed" : $"{Lie(s.EndLie)} {s.EndDistance} {endUnit}";
        var extras = new List<string>();
        if (s.Direction != Domain.Direction.OnTarget)
            extras.Add(Direction(s.Direction));
        if (s.Shape is { } shape)
            extras.Add(shape.ToString());
        if (s.PenaltyStrokes > 0)
            extras.Add($"+{s.PenaltyStrokes} penalty");
        var tail = extras.Count > 0 ? "  ·  " + string.Join(", ", extras) : "";
        return $"{Lie(s.StartLie)} {s.StartDistance} {startUnit}  ·  {Club(s.Club)} → {end}{tail}";
    }

    /// <summary>The derived hole summary in one line once the hole is holed; until then how many strokes so far.</summary>
    public static string SummaryLine(Domain.Holes.HoleSummary? summary, int strokes) => summary is { } h
        ? $"{h.Putts} putt{(h.Putts == 1 ? "" : "s")}  ·  {(h.Gir ? "GIR" : "missed green")}{(h.FairwayHit ? "  ·  fairway" : "")}{(h.PenaltyStrokes > 0 ? $"  ·  {h.PenaltyStrokes} penalty" : "")}"
        : strokes == 0 ? "No shots yet" : $"{strokes} stroke{(strokes == 1 ? "" : "s")} so far";

    public static string ShotHeading(int? editingIndex, int count) =>
        (editingIndex is { } i ? $"Editing shot {i + 1}" : $"Shot {count + 1}").ToUpperInvariant();


    // ---- histograms ----


    public static IReadOnlyList<Bar> Bars(IReadOnlyList<ScoreBucket>? buckets)
    {
        if (buckets is null || buckets.Count == 0)
            return [];
        var max = Math.Max(1, buckets.Max(b => b.Count));
        return buckets.Select(b => new Bar(BucketLabel(b), b.Count, 8 + 200.0 * b.Count / max)).ToList();
    }

    private static string BucketLabel(ScoreBucket b) => (b.Min, b.Max) switch
    {
        (null, { } max) => $"{max} or less",
        ({ } min, null) => $"{min}+",
        ({ } min, { } max) => $"{min}–{max}",
        _ => "",
    };
}

/// <summary>One histogram bar: its label, count and drawn width.</summary>
public sealed record Bar(string Label, int Count, double Width);

/// <summary>Enum values to words in item templates and pickers.</summary>
public sealed partial class EnumTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => Display.Text(value);
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>
/// A TextBox's text to a numeric view model property, updated as the golfer types (so a button tapped straight after typing sees
/// the value). ConverterParameter "int" or "double". Unfinished input ("-", "35.") leaves the property as it was; empty is null.
/// </summary>
public sealed partial class NumberTextConverter : IValueConverter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        int i => i.ToString(Inv),
        double d when double.IsFinite(d) => d.ToString("0.###", Inv),
        _ => "",
    };

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var text = (value as string)?.Trim() ?? "";
        if (text.Length == 0)
            return null;
        if (text.EndsWith('.') || text == "-")
            return DependencyProperty.UnsetValue;
        if ((parameter as string) == "double")
            return double.TryParse(text, NumberStyles.Float, Inv, out var d) ? d : DependencyProperty.UnsetValue;
        return int.TryParse(text, NumberStyles.Integer, Inv, out var i) ? i : DependencyProperty.UnsetValue;
    }
}

/// <summary>NumberBox (double, NaN when empty) to an int? or int view model property.</summary>
public sealed partial class NumberBoxIntConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int i ? (double)i : double.NaN;

    public object? ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is double d && double.IsFinite(d) ? (int)Math.Round(d) : null;
}

/// <summary>NumberBox (double, NaN when empty) to a double? view model property.</summary>
public sealed partial class NumberBoxDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is double d ? d : double.NaN;

    public object? ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is double d && double.IsFinite(d) ? d : null;
}

/// <summary>A view model DateTime to the DatePicker's DateTimeOffset.</summary>
public sealed partial class DateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is DateTime d ? new DateTimeOffset(DateTime.SpecifyKind(d.Date, DateTimeKind.Local)) : DateTimeOffset.Now;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is DateTimeOffset o ? o.LocalDateTime.Date : DateTime.Today;
}
