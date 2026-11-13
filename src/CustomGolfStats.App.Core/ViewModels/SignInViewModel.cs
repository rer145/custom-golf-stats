using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.ApiAccess;
using ApiException = CustomGolfStats.Api.Client.ApiException;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>Sign in or create an account with email and password, or complete an Apple / Google sign-in whose ID token the platform
/// supplies. Tokens go straight to the secure store through <see cref="TokenService"/>; this view model never holds them.</summary>
public sealed partial class SignInViewModel(TokenService tokens, INavigator navigator) : ViewModelBase
{
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";

    /// <summary>True when the form creates an account instead of signing in.</summary>
    [ObservableProperty] private bool _isRegisterMode;

    /// <summary>Goes straight home when a session already exists.</summary>
    public async Task InitializeAsync()
    {
        if (await tokens.IsSignedInAsync())
            await navigator.NavigateAsync(Route.Home);
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        var email = Email.Trim();
        if (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
        {
            ErrorMessage = UiText.EmailInvalid;
            return;
        }
        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = UiText.PasswordMissing;
            return;
        }

        await RunAsync(async () =>
        {
            if (IsRegisterMode)
                await tokens.RegisterAsync(email, Password);
            else
                await tokens.SignInAsync(email, Password);
        });
    }

    /// <summary>Completes Apple or Google sign-in with the ID token the platform obtained.</summary>
    public Task ExternalSignInAsync(string provider, string idToken, string? firstName = null, string? lastName = null) =>
        RunAsync(() => tokens.ExternalSignInAsync(provider, idToken, firstName, lastName));

    private async Task RunAsync(Func<Task> signIn)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await signIn();
            Password = "";
            await navigator.NavigateAsync(Route.Home);
        }
        catch (ApiException ex) when (ex.StatusCode is 400 or 401 or 403)
        {
            ErrorMessage = IsRegisterMode ? UiText.RegisterFailed : UiText.SignInFailed;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = UiText.SignInOffline;
        }
        catch (Exception ex) when (ex is ApiException or IOException or TaskCanceledException)
        {
            ErrorMessage = UiText.SignInError;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
