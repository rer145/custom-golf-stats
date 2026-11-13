using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using CustomGolfStats.App.Platforms.Android.Services;

namespace CustomGolfStats.App.Droid;

[Activity(
    MainLauncher = true,
    ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
    WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateHidden,
    Exported = true
)]
public class MainActivity : Uno.UI.Runtime.Android.ApplicationActivity
{
    /// <summary>The running activity, for runtime permission prompts.</summary>
    internal static MainActivity? Running { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
        base.OnCreate(savedInstanceState);
        Running = this;
    }

    protected override void OnDestroy()
    {
        if (Running == this)
            Running = null;
        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        AndroidPermissions.OnResult(requestCode, grantResults);
    }
}
