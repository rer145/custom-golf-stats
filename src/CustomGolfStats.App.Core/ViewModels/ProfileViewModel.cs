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
    LocalDb db, ICustomGolfStatsApiClient api, TokenService tokens, IConnectivity connectivity, IClock clock, INavigator navigator) : ViewModelBase
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

    [ObservableProperty] private bool _isSaved;

    public async Task LoadAsync()
    {
        NewHandicapDate = clock.UtcNow.LocalDateTime.Date;
        await ReloadHandicapAsync();

        ErrorMessage = null;
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
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            ErrorMessage = UiText.ProfileLoadFailed;
        }
    }

    private async Task ReloadHandicapAsync()
    {
        HandicapHistory.Clear();
        foreach (var entry in await db.Handicap.ListAsync())
            HandicapHistory.Add(entry);
        LatestHandicapIndex = HandicapHistory.Count > 0 ? HandicapHistory[0].Index : null;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        ErrorMessage = null;
        IsSaved = false;
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
        catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or TaskCanceledException)
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
            await ReloadHandicapAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            HandicapErrors.Add(UiText.ProfileSaveFailed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Signs out: the tokens are removed from secure storage. Rounds on this device are kept.</summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        await tokens.SignOutAsync();
        await navigator.NavigateAsync(Route.SignIn);
    }
}
