using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Storage;
using ApiException = CustomGolfStats.Api.Client.ApiException;
using ICustomGolfStatsApiClient = CustomGolfStats.Api.Client.ICustomGolfStatsApiClient;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>Sign in or create an account with email and password, or complete an Apple / Google sign-in whose ID token the platform
/// supplies. Tokens go straight to the secure store through <see cref="TokenService"/>; this view model never holds them.
/// After signing in it checks whether a different account than the one whose data is on this device has signed in: if unsynced
/// rounds would be lost, <see cref="PendingSwitch"/> is set and the golfer must confirm or cancel before going on.</summary>
public sealed partial class SignInViewModel(TokenService tokens, AccountSwitchService accounts, ICustomGolfStatsApiClient api, INavigator navigator) : ViewModelBase
{
    private string? _pendingAccountKey;

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";

    /// <summary>True when the form creates an account instead of signing in.</summary>
    [ObservableProperty] private bool _isRegisterMode;

    /// <summary>Set when a different account signed in while this device holds unsynced data. The UI shows
    /// <see cref="PendingSwitchMessage"/> with Confirm and Cancel; nothing syncs until one is chosen.</summary>
    [ObservableProperty] private AccountSwitchPrompt? _pendingSwitch;

    public string? PendingSwitchMessage => PendingSwitch is { } p ? UiText.UnsyncedFromAnotherAccount(p.UnsyncedRounds, p.PendingCourses) : null;

    partial void OnPendingSwitchChanged(AccountSwitchPrompt? value) => OnPropertyChanged(nameof(PendingSwitchMessage));

    /// <summary>Resumes an unanswered account question, or goes straight home when a session already exists.</summary>
    public async Task InitializeAsync()
    {
        if (await accounts.GetPendingAsync() is { } pending)
        {
            _pendingAccountKey = pending.AccountKey;
            PendingSwitch = pending.Prompt;
            return;
        }
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
        }, email);
    }

    /// <summary>Completes Apple or Google sign-in with the ID token the platform obtained.</summary>
    public Task ExternalSignInAsync(string provider, string idToken, string? firstName = null, string? lastName = null) =>
        RunAsync(() => tokens.ExternalSignInAsync(provider, idToken, firstName, lastName), typedEmail: null);

    /// <summary>The golfer agreed to remove the previous account's unsynced data from this device.</summary>
    [RelayCommand]
    private async Task ConfirmAccountSwitchAsync()
    {
        if (_pendingAccountKey is null)
            return;
        if (!await TryAsync(() => accounts.ConfirmSwitchAsync(_pendingAccountKey)))
            return;
        PendingSwitch = null;
        _pendingAccountKey = null;
        await navigator.NavigateAsync(Route.Home);
    }

    /// <summary>The golfer kept the previous account's data: the new sign-in is undone and nothing is removed.</summary>
    [RelayCommand]
    private async Task CancelAccountSwitchAsync()
    {
        await TryAsync(async () =>
        {
            await tokens.SignOutAsync();
            await accounts.CancelSwitchAsync();
        });
        PendingSwitch = null;
        _pendingAccountKey = null;
    }

    private async Task RunAsync(Func<Task> signIn, string? typedEmail)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            await signIn();

            var key = await AccountKeyAsync(typedEmail);
            if (key is null)
            {
                await tokens.SignOutAsync(); // never carry on without knowing whose data this is
                ErrorMessage = UiText.AccountUnknown;
                return;
            }
            if (await accounts.CheckAsync(key) is { } prompt)
            {
                _pendingAccountKey = key;
                PendingSwitch = prompt;
                return;
            }

            Password = "";
            await navigator.NavigateAsync(Route.Home);
        }
        catch (AccountCreatedSignInFailedException)
        {
            IsRegisterMode = false;
            ErrorMessage = UiText.AccountCreatedSignInFailed;
        }
        catch (ApiException ex) when (ex.StatusCode is 400 or 401 or 403)
        {
            ErrorMessage = IsRegisterMode ? UiText.RegisterFailed : UiText.SignInFailed;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = UiText.SignInOffline;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = UiText.SignInError;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The account's email as the server knows it, falling back to what was typed. Null when it cannot be determined.</summary>
    private async Task<string?> AccountKeyAsync(string? typedEmail)
    {
        try
        {
            var profile = await api.MeGetProfileAsync();
            if (!string.IsNullOrWhiteSpace(profile.Email))
                return AccountSwitchService.KeyFor(profile.Email);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            // fall through to the typed email
        }
        return typedEmail is null ? null : AccountSwitchService.KeyFor(typedEmail);
    }
}
