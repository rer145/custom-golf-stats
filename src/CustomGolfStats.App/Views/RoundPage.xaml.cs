namespace CustomGolfStats.App.Views;

/// <summary>Route.Round: binds <see cref="RoundViewModel"/>.</summary>
public sealed partial class RoundPage : RoutePage
{
    public RoundPage()
    {
        InitializeComponent();
    }

    public RoundViewModel ViewModel { get; } = Resolve<RoundViewModel>();

    protected override Task LoadAsync(object? parameter) => LoadRoundAsync((RoundArgs)parameter!);

    private async Task LoadRoundAsync(RoundArgs args)
    {
        await ViewModel.LoadAsync(args);
        await Resolve<CustomGolfStats.App.Platform.IMotionPermission>().PrepareAsync();
    }

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
