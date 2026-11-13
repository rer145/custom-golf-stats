namespace CustomGolfStats.App.Views;

/// <summary>Route.ShotEntry: binds <see cref="ShotEntryViewModel"/>.</summary>
public sealed partial class ShotEntryPage : RoutePage
{
    public ShotEntryPage()
    {
        InitializeComponent();
    }

    public ShotEntryViewModel ViewModel { get; } = Resolve<ShotEntryViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((HoleArgs)parameter!);

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
