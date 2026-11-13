namespace CustomGolfStats.App.Views;

/// <summary>Route.QuickHole: binds <see cref="QuickHoleViewModel"/>.</summary>
public sealed partial class QuickHolePage : RoutePage
{
    public QuickHolePage()
    {
        InitializeComponent();
    }

    public QuickHoleViewModel ViewModel { get; } = Resolve<QuickHoleViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((HoleArgs)parameter!);

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
