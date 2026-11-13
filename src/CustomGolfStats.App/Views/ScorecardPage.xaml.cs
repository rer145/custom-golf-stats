namespace CustomGolfStats.App.Views;

/// <summary>Route.Scorecard: binds <see cref="ScorecardViewModel"/>.</summary>
public sealed partial class ScorecardPage : RoutePage
{
    public ScorecardPage()
    {
        InitializeComponent();
    }

    public ScorecardViewModel ViewModel { get; } = Resolve<ScorecardViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((RoundArgs)parameter!);

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
