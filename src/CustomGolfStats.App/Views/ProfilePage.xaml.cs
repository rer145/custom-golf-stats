namespace CustomGolfStats.App.Views;

/// <summary>Route.Profile: binds <see cref="ProfileViewModel"/>.</summary>
public sealed partial class ProfilePage : RoutePage
{
    public ProfilePage()
    {
        InitializeComponent();
    }

    public ProfileViewModel ViewModel { get; } = Resolve<ProfileViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
