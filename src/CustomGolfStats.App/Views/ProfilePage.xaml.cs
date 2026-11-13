namespace CustomGolfStats.App.Views;

/// <summary>Route.Profile: binds <see cref="ProfileViewModel"/>: account fields, handicap history and sign out.</summary>
public sealed partial class ProfilePage : RoutePage
{
    public ProfilePage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public ProfileViewModel ViewModel { get; } = Resolve<ProfileViewModel>();

    /// <summary>The API's unit values.</summary>
    public static IReadOnlyList<string> UnitChoices { get; } = ["Imperial", "Metric"];

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();
}
