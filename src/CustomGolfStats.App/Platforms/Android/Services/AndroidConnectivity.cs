using Android.Content;
using Android.Net;

namespace CustomGolfStats.App.Platforms.Android.Services;

/// <summary>Online when the active network offers internet access.</summary>
public sealed class AndroidConnectivity(Context context) : IConnectivity
{
    public bool IsOnline
    {
        get
        {
            if (context.GetSystemService(Context.ConnectivityService) is not global::Android.Net.ConnectivityManager manager)
                return true;
            var capabilities = manager.GetNetworkCapabilities(manager.ActiveNetwork);
            return capabilities?.HasCapability(NetCapability.Internet) == true;
        }
    }
}
