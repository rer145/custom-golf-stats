namespace CustomGolfStats.App.Views;

/// <summary>Route.RoundSummary: binds <see cref="RoundSummaryViewModel"/>.</summary>
public sealed partial class RoundSummaryPage : RoutePage
{
    public RoundSummaryPage()
    {
        InitializeComponent();
    }

    public RoundSummaryViewModel ViewModel { get; } = Resolve<RoundSummaryViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((RoundArgs)parameter!);

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
