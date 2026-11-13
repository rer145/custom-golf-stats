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

    public static string SubmitText(bool register) => register ? "Create account" : "Sign in";

    public static string ToggleText(bool register) => register ? "I already have an account" : "Create an account";
}
