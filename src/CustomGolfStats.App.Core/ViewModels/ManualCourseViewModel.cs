using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain.Courses;
using CustomGolfStats.Domain.Holes;
using CourseSource = CustomGolfStats.Api.Client.CourseSource;
using CourseStatus = CustomGolfStats.Api.Client.CourseStatus;
using Gender = CustomGolfStats.Api.Client.Gender;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>One hole being typed in.</summary>
public sealed partial class ManualHoleEntry(int number) : ObservableObject
{
    public int Number { get; } = number;
    [ObservableProperty] private int _par = 4;
    [ObservableProperty] private int _yardage = 350;
    [ObservableProperty] private int _strokeIndex = number;
}

/// <summary>Create a course by hand, offline. It gets client-generated ids, is saved as pending and goes to the server on the next sync.</summary>
public sealed partial class ManualCourseViewModel(LocalDb db, IClock clock, INavigator navigator) : ViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _clubName;
    [ObservableProperty] private string? _city;
    [ObservableProperty] private string? _region;
    [ObservableProperty] private string? _country;
    [ObservableProperty] private string _teeName = UiText.DefaultTeeName;
    [ObservableProperty] private Gender _gender = Gender.Unspecified;
    [ObservableProperty] private double? _rating;
    [ObservableProperty] private int? _slope;

    public IReadOnlyList<Gender> Genders { get; } = Enum.GetValues<Gender>();
    public ObservableCollection<ManualHoleEntry> Holes { get; } = [];
    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>Starts a blank form with 9 or 18 holes (call once when the screen opens).</summary>
    public void Initialize(int holeCount = 18) => SetHoleCount(holeCount);

    /// <summary>Resizes the hole list to 9 or 18, keeping what was typed.</summary>
    [RelayCommand]
    private void SetHoleCount(int count)
    {
        while (Holes.Count > count)
            Holes.RemoveAt(Holes.Count - 1);
        while (Holes.Count < count)
            Holes.Add(new ManualHoleEntry(Holes.Count + 1));
    }

    /// <summary>The problems with what was entered, in the golfer's words. Empty means it can be saved. The limits are the domain's
    /// <see cref="CourseValidator"/>, the same ones the API applies, so a course accepted here is accepted on sync.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add(UiText.CourseNameMissing);
        if (string.IsNullOrWhiteSpace(TeeName))
            errors.Add(UiText.TeeNameMissing);

        TooLong(Name, CourseLimits.MaxNameLength, errors);
        TooLong(ClubName, CourseLimits.MaxClubNameLength, errors);
        TooLong(TeeName, CourseLimits.MaxTeeNameLength, errors);
        TooLong(City, CourseLimits.MaxCityLength, errors);
        TooLong(Region, CourseLimits.MaxRegionLength, errors);
        TooLong(Country, CourseLimits.MaxCountryLength, errors);

        var layout = Holes.Select(h => new HoleInfo(h.Number, h.Par, h.Yardage, h.StrokeIndex)).ToList();
        // A cleared number box reports NaN: treat a non-finite rating as missing rather than letting the decimal conversion throw.
        decimal? rating = Rating is { } r && double.IsFinite(r) ? (decimal)r : null;
        errors.AddRange(CourseValidator.Validate(rating, Slope, layout).Select(UiText.Describe));
        return errors;
    }

    private static void TooLong(string? text, int max, List<string> errors)
    {
        if (text is not null && text.Trim().Length > max && !errors.Contains(UiText.TextTooLong(max)))
            errors.Add(UiText.TextTooLong(max));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Errors.Clear();
        foreach (var error in Validate())
            Errors.Add(error);
        if (Errors.Count > 0)
            return;

        var courseId = Guid.NewGuid();
        var teeId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var holes = Holes.Select(h => new TeeHoleRow { TeeVersionId = versionId, Number = h.Number, Par = h.Par, Yardage = h.Yardage, StrokeIndex = h.StrokeIndex }).ToList();
        var version = new TeeVersionRow
        {
            // In effect for every date, like the first version the API creates for a tee.
            Id = versionId, TeeId = teeId, EffectiveFrom = CourseLimits.DefaultEffectiveFrom.ToDateTime(TimeOnly.MinValue), Par = holes.Sum(h => h.Par),
            Rating = Rating!.Value, Slope = Slope!.Value,
        };
        var course = new CourseRow
        {
            Id = courseId, Name = Name.Trim(), ClubName = Clean(ClubName), City = Clean(City), Region = Clean(Region), Country = Clean(Country),
            Status = CourseStatus.UserEdited, Source = CourseSource.Manual, UpdatedAt = clock.UtcNow,
        };
        var tee = new TeeRow { Id = teeId, CourseId = courseId, Name = TeeName.Trim(), Gender = Gender };
        var record = new CourseRecord(course, [new TeeRecord(tee, [new TeeVersionRecord(version, holes)])]);
        if (await TryAsync(() => db.Courses.SaveLocalAsync(record)))
            await navigator.NavigateAsync(Route.StartRound, new CourseArgs(courseId));
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
