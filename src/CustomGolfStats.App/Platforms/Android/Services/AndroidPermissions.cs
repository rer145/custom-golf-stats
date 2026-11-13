using System.Collections.Concurrent;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace CustomGolfStats.App.Platforms.Android.Services;

/// <summary>Runtime permission requests through the main activity (which forwards the results here).</summary>
public static class AndroidPermissions
{
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> Pending = new();
    private static int _nextCode = 4200;

    public static bool IsGranted(string permission) =>
        ContextCompat.CheckSelfPermission(global::Android.App.Application.Context, permission) == Permission.Granted;

    /// <summary>True when granted (now or before). False when denied or when there is no activity to ask from.</summary>
    public static Task<bool> RequestAsync(string permission)
    {
        if (IsGranted(permission))
            return Task.FromResult(true);
        if (CustomGolfStats.App.Droid.MainActivity.Running is not { } activity)
            return Task.FromResult(false);

        var code = Interlocked.Increment(ref _nextCode);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[code] = tcs;
        activity.RunOnUiThread(() => ActivityCompat.RequestPermissions(activity, [permission], code));
        return tcs.Task;
    }

    internal static void OnResult(int requestCode, Permission[] results)
    {
        if (Pending.TryRemove(requestCode, out var tcs))
            tcs.TrySetResult(results.Length > 0 && results.All(r => r == Permission.Granted));
    }
}
