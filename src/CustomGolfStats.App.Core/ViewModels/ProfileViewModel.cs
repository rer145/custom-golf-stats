using CustomGolfStats.App.Core.Sync;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using ApiException = CustomGolfStats.Api.Client.ApiException;
using CreateHandicapEntryRequest = CustomGolfStats.Api.Client.CreateHandicapEntryRequest;
using DeleteAccountRequest = CustomGolfStats.Api.Client.DeleteAccountRequest;
using ICustomGolfStatsApiClient = CustomGolfStats.Api.Client.ICustomGolfStatsApiClient;
using UpdateProfileRequest = CustomGolfStats.Api.Client.UpdateProfileRequest;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>The golfer's profile and handicap history. The handicap history is read from this device (it arrives with sync); the
/// profile and adding a handicap entry need a connection. <c>PUT /me</c> replaces the whole profile, so every field is sent.</summary>
public sealed partial class ProfileViewModel(
    LocalDb db, ICustomGolfStatsApiClient api, TokenService tokens, SyncEngine sync, AccountSwitchService accounts, IConnectivity connectivity, IClock clock,
    INavigator navigator) : ViewModelBase
{
    /// <summary>The password typed into the delete-account dialog. Cleared once the attempt is over, whatever its outcome.</summary>
    [ObservableProperty] private string _deletePassword = "";

    /// <summary>Why the account was not deleted (or that the device could not be fully cleared), or null.</summary>
    [ObservableProperty] private string? _deleteError;

    private string _gender = "";
    private Guid? _homeCourseId;

    public ObservableCollection<HandicapRow> HandicapHistory { get; } = [];
    public ObservableCollection<string> HandicapErrors { get; } = [];

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string? _ghinNumber;
    [ObservableProperty] private string _units = "";
    [ObservableProperty] private double? _latestHandicapIndex;

    // New handicap entry.
    [ObservableProperty] private double? _newHandicapIndex;
    [ObservableProperty] private DateTime _newHandicapDate = DateTime.Today;

    /// <summary>The index as typed: "+2.1" is a plus handicap (stored as -2.1), "12.4" a normal one (see <see cref="HandicapText"/>).
    /// Sets <see cref="NewHandicapIndex"/>; text that cannot be read leaves it empty and adding reports it.</summary>
    [ObservableProperty] private string _newHandicapText = "";

    partial void OnNewHandicapTextChanged(string value) =>
        NewHandicapIndex = HandicapText.TryParse(value, out var index) ? index : null;

    [ObservableProperty] private bool _isSaved;

    /// <summary>The profile was read from the server. Saving is only possible then, because <c>PUT /me</c> replaces everything and
    /// saving fields that were never loaded would blank the account.</summary>
    [ObservableProperty] private bool _isLoaded;

    public async Task LoadAsync()
    {
        NewHandicapDate = clock.UtcNow.LocalDateTime.Date;
        await ReloadHandicapAsync();

        ErrorMessage = null;
        IsLoaded = false;
        if (!connectivity.IsOnline)
        {
            ErrorMessage = UiText.ProfileLoadFailed;
            return;
        }
        try
        {
            var profile = await api.MeGetProfileAsync();
            Email = profile.Email; FirstName = profile.FirstName; LastName = profile.LastName; GhinNumber = profile.GhinNumber; Units = profile.Units;
            _gender = profile.Gender;
            _homeCourseId = profile.HomeCourseId;
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            ErrorMessage = UiText.ProfileLoadFailed;
        }
    }

    partial void OnIsLoadedChanged(bool value) => SaveProfileCommand.NotifyCanExecuteChanged();

    private async Task ReloadHandicapAsync()
    {
        HandicapHistory.Clear();
        foreach (var entry in await db.Handicap.ListAsync())
            HandicapHistory.Add(entry);
        LatestHandicapIndex = HandicapHistory.Count > 0 ? HandicapHistory[0].Index : null;
    }

    [RelayCommand(CanExecute = nameof(IsLoaded))]
    private async Task SaveProfileAsync()
    {
        ErrorMessage = null;
        IsSaved = false;
        if (!IsLoaded)
            return;
        if (!connectivity.IsOnline)
        {
            ErrorMessage = UiText.NeedConnection;
            return;
        }
        IsBusy = true;
        try
        {
            await api.MeUpdateProfileAsync(new UpdateProfileRequest
            {
                FirstName = FirstName.Trim(), LastName = LastName.Trim(), Gender = _gender, GhinNumber = string.IsNullOrWhiteSpace(GhinNumber) ? null : GhinNumber.Trim(),
                HomeCourseId = _homeCourseId, Units = Units,
            });
            IsSaved = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = UiText.ProfileSaveFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adds a handicap index as of a date. The server stores it and the entry is kept on this device.</summary>
    [RelayCommand]
    private async Task AddHandicapAsync()
    {
        HandicapErrors.Clear();
        if (NewHandicapIndex is not (>= -10.0 and <= 54.0))
        {
            HandicapErrors.Add(UiText.HandicapInvalid);
            return;
        }
        if (!connectivity.IsOnline)
        {
            HandicapErrors.Add(UiText.NeedConnection);
            return;
        }
        IsBusy = true;
        try
        {
            var created = await api.MeAddHandicapAsync(new CreateHandicapEntryRequest
            {
                EffectiveOn = new DateTimeOffset(DateTime.SpecifyKind(NewHandicapDate.Date, DateTimeKind.Unspecified), TimeSpan.Zero),
                Index = NewHandicapIndex,
            });
            await db.Handicap.UpsertAsync([new HandicapRow { Id = created.Id, EffectiveOn = created.EffectiveOn.DateTime.Date, Index = created.Index }]);
            NewHandicapIndex = null;
            NewHandicapText = "";
            await ReloadHandicapAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HandicapErrors.Add(UiText.ProfileSaveFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The confirm dialog's text: deletion is permanent, and anything only this device holds (unsynced rounds, courses not yet
    /// saved, holes being entered) is lost with it.</summary>
    public async Task<string> DescribeAccountDeletionAsync()
    {
        var unsynced = await accounts.DescribeUnsyncedAsync();
        return UiText.DeleteAccountWarning(unsynced.UnsyncedRounds, unsynced.PendingCourses, unsynced.UnsavedHoles);
    }

    /// <summary>
    /// Deletes the account on the server (<c>DELETE /me</c>, confirmed with <see cref="DeletePassword"/>), then clears this device and
    /// returns to sign-in. Sync is blocked and any running sync stopped BEFORE the request, so nothing is pushed for an account that
    /// is being deleted; if the server does not delete it, sync is unblocked again and nothing local is touched. Once the server has
    /// deleted it (or says it is already gone), every local table and the stored tokens are removed.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        DeleteError = null;
        if (IsBusy)
            return;
        var password = DeletePassword;
        DeletePassword = "";
        if (string.IsNullOrEmpty(password))
        {
            DeleteError = UiText.DeleteAccountPasswordMissing;
            return;
        }
        if (!connectivity.IsOnline)
        {
            DeleteError = UiText.NeedConnection;
            return;
        }

        IsBusy = true;
        var deleted = false;
        try
        {
            try
            {
                await accounts.BeginAccountDeletionAsync();
                await api.MeDeleteAccountAsync(new DeleteAccountRequest { Password = password });
                deleted = true;
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                deleted = true; // already gone (deleted from another device): finish the job here
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                DeleteError = UiText.DeleteAccountWrongPassword;
            }
            catch (ApiException ex) when (ex.StatusCode == 429)
            {
                DeleteError = UiText.DeleteAccountLockedOut;
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                DeleteError = UiText.DeleteAccountLastAdmin;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DeleteError = UiText.DeleteAccountFailed;
            }
        }
        finally
        {
            if (deleted)
                await FinishDeletionAsync();
            else
                await AbandonDeletionQuietlyAsync();
            IsBusy = false;
        }

        if (deleted)
            await navigator.NavigateAsync(Route.SignIn);
    }

    /// <summary>The server deleted the account: clear the device, then the tokens. The tokens go even if clearing failed (they can't
    /// be used any more anyway); a marker left by a failed clear keeps sync blocked.</summary>
    private async Task FinishDeletionAsync()
    {
        try
        {
            await accounts.ForgetDeletedAccountAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DeleteError = UiText.DeleteAccountCleanupFailed;
        }
        finally
        {
            await tokens.SignOutAsync();
        }
    }

    private async Task AbandonDeletionQuietlyAsync()
    {
        try
        {
            await accounts.AbandonAccountDeletionAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The marker stays and keeps sync blocked, and the next launch treats it as an interrupted sign-in (it signs out).
        }
    }

    /// <summary>Signs out: a running sync is stopped first (so no request in flight outlives the session), then the tokens are
    /// removed from secure storage. Rounds on this device are kept.</summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (!await TryAsync(async () =>
            {
                await sync.StopAsync();
                await tokens.SignOutAsync();
            }))
            return;
        await navigator.NavigateAsync(Route.SignIn);
    }
}
