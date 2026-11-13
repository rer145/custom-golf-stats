using CustomGolfStats.App.Core.Sync;
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
    public const string SignInError = "Something went wrong signing in. Please try again.";

    // ---- courses ----
    public const string CourseNameMissing = "Enter the course name.";
    public const string TeeNameMissing = "Enter the tee name.";
    public const string HoleCountInvalid = "A course has 9 or 18 holes.";
    public const string RatingInvalid = "Enter the course rating, for example 71.4.";
    public const string SlopeInvalid = "Slope must be between 55 and 155.";
    public const string ParInvalid = "Par must be 3, 4 or 5.";
    public const string YardageInvalid = "Enter a yardage for every hole.";
    public const string StrokeIndexInvalid = "Stroke index must use each number from 1 to the number of holes once.";
    public const string ProviderSearchOffline = "Online course search needs a connection. Your saved courses are shown below.";
    public const string ProviderSearchFailed = "Online course search is unavailable right now. Your saved courses are shown below.";
    public const string ImportFailed = "That course could not be imported right now. Try again later or add it by hand.";

    // ---- starting a round ----
    public const string ChooseTee = "Choose a tee.";
    public const string CourseNotFound = "That course is no longer on this device.";
    public const string CourseHasNoTees = "This course has no tees yet.";

    // ---- extras / profile ----
    public const string NegativeNumber = "Enter zero or more.";
    public const string WindDirectionInvalid = "Wind direction is between 0 and 359 degrees.";
    public const string HandicapInvalid = "A handicap index is between -10.0 and 54.0.";
    public const string NeedConnection = "This needs a connection.";
    public const string ProfileSaveFailed = "Your profile could not be saved. Please try again.";
    public const string ProfileLoadFailed = "Your profile could not be loaded. Showing what is saved on this device.";

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
