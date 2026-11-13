namespace CustomGolfStats.App.Views;

/// <summary>Route.OverallStats: binds <see cref="OverallStatsViewModel"/>.</summary>
public sealed partial class OverallStatsPage : RoutePage
{
    public OverallStatsPage()
    {
        InitializeComponent();
    }

    public OverallStatsViewModel ViewModel { get; } = Resolve<OverallStatsViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
