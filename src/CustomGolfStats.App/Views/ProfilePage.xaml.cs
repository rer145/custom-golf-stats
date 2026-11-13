using CustomGolfStats.App.Diagnostics;
using Microsoft.UI.Xaml.Automation;

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

    /// <summary>The API's distance unit values (DistanceUnits).</summary>
    public static IReadOnlyList<string> UnitChoices { get; } = ["Yards", "Meters"];

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();

    public static string DeleteAccountText => UiText.DeleteAccountButton;
    public static string DeleteAccountHint => UiText.DeleteAccountHint;

    /// <summary>Delete account: one dialog that says what is lost (including rounds only this device holds) and asks for the password.
    /// The password goes straight to the view model and is never kept by the page.</summary>
    private async void OnDeleteAccount(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = new PasswordBox { Header = UiText.DeleteAccountPasswordLabel };
            AutomationProperties.SetName(password, UiText.DeleteAccountPasswordLabel);
            var content = new StackPanel { Spacing = 16 };
            content.Children.Add(new TextBlock { Text = await ViewModel.DescribeAccountDeletionAsync(), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(password);

            if (!await ConfirmAsync(UiText.DeleteAccountTitle, content, UiText.DeleteAccountConfirm))
                return;
            ViewModel.DeletePassword = password.Password;
            password.Password = "";
            await ViewModel.DeleteAccountCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            CrashReporter.Report("deleting the account", ex);
        }
    }
}
