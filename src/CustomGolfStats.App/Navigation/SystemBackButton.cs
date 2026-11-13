using Windows.UI.Core;

namespace CustomGolfStats.App.Navigation;

/// <summary>The Android system back gesture/button goes back in the frame while there is somewhere to go back to.</summary>
public static class SystemBackButton
{
    public static void Attach(Frame frame)
    {
        SystemNavigationManager.GetForCurrentView().BackRequested += (_, e) =>
        {
            if (e.Handled || !frame.CanGoBack)
                return;
            e.Handled = true;
            frame.GoBack();
        };
    }
}
