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
using ICustomGolfStatsApiClient = CustomGolfStats.Api.Client.ICustomGolfStatsApiClient;
using UpdateProfileRequest = CustomGolfStats.Api.Client.UpdateProfileRequest;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>The golfer's profile and handicap history. The handicap history is read from this device (it arrives with sync); the
/// profile and adding a handicap entry need a connection. <c>PUT /me</c> replaces the whole profile, so every field is sent.</summary>
public sealed partial class ProfileViewModel(
    LocalDb db, ICustomGolfStatsApiClient api, TokenService tokens, SyncEngine sync, IConnectivity connectivity, IClock clock, INavigator navigator) : ViewModelBase
{
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
