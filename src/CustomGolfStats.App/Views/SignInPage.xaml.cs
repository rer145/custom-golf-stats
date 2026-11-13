namespace CustomGolfStats.App.Views;

/// <summary>Route.SignIn: binds <see cref="SignInViewModel"/>.</summary>
public sealed partial class SignInPage : RoutePage
{
    public SignInPage()
    {
        InitializeComponent();
    }

    public SignInViewModel ViewModel { get; } = Resolve<SignInViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.InitializeAsync();

    /// <summary>The stored session could not be checked because the device was offline: offer to check again now.</summary>
    public static Visibility OfflineRetryVisibility(string? error) =>
        error == UiText.VerifySessionOffline ? Visibility.Visible : Visibility.Collapsed;

    private async void OnRetryVerify(object sender, RoutedEventArgs e)
    {
        ViewModel.ErrorMessage = null;
        await ViewModel.InitializeAsync();
    }

    public static string SubmitText(bool register) => register ? "Create account" : "Sign in";

    public static string ToggleText(bool register) => register ? "I already have an account" : "Create an account";
}
