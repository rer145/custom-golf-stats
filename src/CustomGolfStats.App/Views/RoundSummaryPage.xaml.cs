using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.App.Views;

/// <summary>Route.RoundSummary: binds <see cref="RoundSummaryViewModel"/>: totals, extras editing, scorecard, delete.</summary>
public sealed partial class RoundSummaryPage : RoutePage
{
    public RoundSummaryPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public RoundSummaryViewModel ViewModel { get; } = Resolve<RoundSummaryViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((RoundArgs)parameter!);

    public static string Gir(SeasonResultRow? row) => row is null ? UiText.NoValue : Display.Percent(row.Gir);
    public static string Fairways(SeasonResultRow? row) => row is null ? UiText.NoValue : Display.Percent(row.DrivingAccuracy);

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync("Delete this round?", "The round is removed from this device and from your account when you next sync. This cannot be undone.", "Delete round"))
            await ViewModel.DeleteRoundCommand.ExecuteAsync(null);
    }
}
