using System.Globalization;

namespace CustomGolfStats.App.Views;

/// <summary>Route.Scorecard: binds <see cref="ScorecardViewModel"/>. One row per playing, in the order played.</summary>
public sealed partial class ScorecardPage : RoutePage
{
    public ScorecardPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public ScorecardViewModel ViewModel { get; } = Resolve<ScorecardViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((RoundArgs)parameter!);

    public static string Subtitle(string course, string tee, double? rating, int? slope) =>
        $"{course}  ·  {tee}" + (rating is { } r && slope is { } s ? $"  ·  {r.ToString("0.0", CultureInfo.InvariantCulture)} / {s}" : "");
}
