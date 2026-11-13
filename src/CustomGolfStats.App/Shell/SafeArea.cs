namespace CustomGolfStats.App.Shell;

/// <summary>The status-bar / notch inset at the top of the window. The shell leaves it to the pages so a page with a header band can
/// draw the band underneath the status bar; other pages simply start below it (see RoutePage).</summary>
public static class SafeArea
{
    public static double Top { get; private set; }

    public static event Action? TopChanged;

    internal static void SetTop(double value)
    {
        if (Math.Abs(value - Top) < 0.5)
            return;
        Top = value;
        TopChanged?.Invoke();
    }
}
