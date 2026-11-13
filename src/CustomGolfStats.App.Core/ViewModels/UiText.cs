using CustomGolfStats.App.Core.Sync;
using CustomGolfStats.Domain.Courses;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>Every sentence a view model shows the golfer, in one place so the app can localise it later.</summary>
public static class UiText
{
    // ---- hole and shot validation ----
    public static string Describe(HoleSummaryError error) => error switch
    {
        HoleSummaryError.ScoreBelowOne => "The score must be at least 1.",
        HoleSummaryError.NegativePutts => "Putts cannot be negative.",
        HoleSummaryError.PuttsExceedScore => "Putts must be fewer than the score.",
        HoleSummaryError.NegativePenalty => "Penalty strokes cannot be negative.",
        HoleSummaryError.NegativeSand => "Sand shots cannot be negative.",
        HoleSummaryError.PenaltiesAndPuttsExceedScore => "Putts plus penalty strokes leave no room for a shot to the green.",
        HoleSummaryError.TeeFlagsOnPar3 => "Fairway and tee shot direction only apply to par 4s and 5s.",
        _ => error.ToString(),
    };

    public static string Describe(ShotError error) => error switch
    {
        ShotError.Empty => "Add at least one shot.",
        ShotError.SequenceNotContiguous => "The shots are out of order.",
        ShotError.InvalidStartLie => "A shot cannot start from the hole, a penalty area or out of bounds.",
        ShotError.HoledBeforeLastShot => "The ball was already holed; remove the shots after it.",
        ShotError.NotHoled => "The last shot must hole out.",
        ShotError.EndDistanceMismatch => "Enter the distance left, or choose Holed.",
        ShotError.NegativeDistance => "Distances cannot be negative.",
        ShotError.NegativePenalty => "Penalty strokes cannot be negative.",
        _ => error.ToString(),
    };

    public const string WrongEntryModeQuick = "This round is entered shot by shot. Open the hole to edit its shots.";
    public const string WrongEntryModeShots = "This round uses quick entry.";
    public const string HoleNotOnCourse = "That hole is not on this tee.";
    public const string RoundNotFound = "That round is no longer on this device.";
    public const string CannotFinishEmptyRound = "Add at least one hole before finishing the round.";

    // ---- entitlements ----
    public const string ShotByShotNeedsAccount = "Shot-by-shot entry needs your account details. Connect to the internet once to load them.";
    public const string ShotByShotTrialUsedUp = "You have used your free shot-by-shot rounds. Upgrade to keep logging every shot.";
    public const string ShotByShotNotAvailable = "Shot-by-shot entry is not available on your plan.";
    public static string TrialRoundsLeft(int count) => count == 1 ? "1 free shot-by-shot round left." : $"{count} free shot-by-shot rounds left.";

    // ---- sign in ----
    public const string EmailInvalid = "Enter a valid email address.";
    public const string PasswordMissing = "Enter your password.";
    public const string SignInFailed = "That email and password did not match.";
    public const string RegisterFailed = "We could not create that account. Check the details or try signing in.";
    public const string SignInOffline = "You need a connection to sign in.";
    public const string AccountCreatedSignInFailed = "Your account was created, but signing in did not work. Try signing in.";
    public const string AccountUnknown = "We could not confirm which account this is. Please try again.";
    public static string UnsyncedFromAnotherAccount(int rounds, int courses) =>
        $"This device has {rounds} round{(rounds == 1 ? "" : "s")}" + (courses > 0 ? $" and {courses} course{(courses == 1 ? "" : "s")}" : "") +
        " that were never synced to the previous account. Signing in as someone else will remove them from this device.";
    public const string SignInError = "Something went wrong signing in. Please try again.";

    // ---- courses ----
    public const string CourseNameMissing = "Enter the course name.";
    public const string TeeNameMissing = "Enter the tee name.";
    public const string DefaultTeeName = "White";
    public static string TextTooLong(int max) => $"Use at most {max} characters.";

    /// <summary>The message for a tee layout problem; every number comes from <see cref="CourseLimits"/>, the same limits the server enforces.</summary>
    public static string Describe(CourseError error) => error switch
    {
        CourseError.RatingMissing => "Enter the course rating, for example 71.4.",
        CourseError.RatingOutOfRange => $"The course rating must be between {CourseLimits.MinRating:0.0} and {CourseLimits.MaxRating:0.0}.",
        CourseError.SlopeMissing => "Enter the slope rating, for example 125.",
        CourseError.SlopeOutOfRange => $"Slope must be between {CourseLimits.MinSlope} and {CourseLimits.MaxSlope}.",
        CourseError.NoHoles => "Add at least one hole.",
        CourseError.TooManyHoles => $"A tee has at most {CourseLimits.MaxHoles} holes.",
        CourseError.HoleNumberOutOfRange => $"Hole numbers are between 1 and {CourseLimits.MaxHoles}.",
        CourseError.HoleNumberRepeated => "A hole number is used more than once.",
        CourseError.ParOutOfRange => $"Par must be between {CourseLimits.MinPar} and {CourseLimits.MaxPar}.",
        CourseError.YardageOutOfRange => $"Yardage must be between {CourseLimits.MinYardage} and {CourseLimits.MaxYardage}.",
        CourseError.StrokeIndexOutOfRange => "Stroke index must be between 1 and the number of holes.",
        CourseError.StrokeIndexRepeated => "Each stroke index from 1 to the number of holes must be used once.",
        _ => error.ToString(),
    };

    public static string TeeDescription(int par, double rating, int slope) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Par {par} · {rating:0.0}/{slope}");

    public const string ProviderSearchOffline = "Online course search needs a connection. Your saved courses are shown below.";
    public const string ProviderSearchFailed = "Online course search is unavailable right now. Your saved courses are shown below.";
    public const string ImportFailed = "That course could not be imported right now. Try again later or add it by hand.";

    // ---- starting a round ----
    public const string ChooseTee = "Choose a tee.";
    public const string CourseNotFound = "That course is no longer on this device.";
    public const string CourseHasNoTees = "This course has no tees yet.";

    // ---- extras / profile ----
    public const string SomethingWentWrong = "Something went wrong. Please try again.";
    public const string CouldNotStartRound = "The round could not be started. Please try again.";
    public const string NotANumber = "Enter a number.";
    public const string NegativeNumber = "Enter zero or more.";
    public const string WindDirectionInvalid = "Wind direction is between 0 and 359 degrees.";
    public const string HandicapInvalid = "A handicap index is between -10.0 and 54.0.";
    public const string NeedConnection = "This needs a connection.";
    public const string ProfileSaveFailed = "Your profile could not be saved. Please try again.";
    public const string ProfileLoadFailed = "Your profile could not be loaded. Your handicap history below is saved on this device.";
    public const string SyncFailedUnexpectedly = "Syncing stopped unexpectedly. Your changes are safe on this device; try again in a moment.";
    public const string NoValue = "-";
    public const string UnitFeet = "ft";
    public const string UnitYards = "yd";

    // ---- sync status ----
    public const string StatusNeverSynced = "Not synced yet";
    public const string StatusSynced = "All changes saved to your account";
    public const string StatusOffline = "Offline. Your changes are safe on this device.";
    public const string StatusSignedOut = "Sign in again to sync.";
    public const string StatusSyncing = "Syncing...";
    public static string StatusWaiting(int count) => count == 1 ? "1 change waiting to sync" : $"{count} changes waiting to sync";
    public static string StatusProblems(int count) => count == 1 ? "1 item could not sync" : $"{count} items could not sync";

    public static string Message(SyncNotice notice) => notice.Message;

    // ---- scores ----
    public static string ToPar(int toPar) => toPar == 0 ? "E" : toPar > 0 ? $"+{toPar}" : toPar.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
