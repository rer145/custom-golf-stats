namespace CustomGolfStats.App.Views;

/// <summary>Route.Home: binds <see cref="HomeViewModel"/>.</summary>
public sealed partial class HomePage : RoutePage
{
    public HomePage()
    {
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; } = Resolve<HomeViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();
}
