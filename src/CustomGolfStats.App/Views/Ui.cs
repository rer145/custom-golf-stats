namespace CustomGolfStats.App.Views;

/// <summary>Small x:Bind helpers (function bindings), so pages need no converters.</summary>
public static class Ui
{
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfSet(object? value) => value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility CollapsedIfSet(object? value) => VisibleIfSet(value) == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;
    public static Visibility VisibleIfZero(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfPositive(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Labels use the mono label style, which is upper case by the brand rules (a style cannot transform text).</summary>
    public static string Label(string? text) => (text ?? "").ToUpperInvariant();
}
