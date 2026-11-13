namespace CustomGolfStats.App.Views;

/// <summary>Route.StartRound: binds <see cref="StartRoundViewModel"/>.</summary>
public sealed partial class StartRoundPage : RoutePage
{
    public StartRoundPage()
    {
        InitializeComponent();
    }

    public StartRoundViewModel ViewModel { get; } = Resolve<StartRoundViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((CourseArgs)parameter!);

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
