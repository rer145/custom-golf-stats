using System.Reflection;

namespace CustomGolfStats.App.Composition;

/// <summary>Build-time configuration. The API base URL comes from the <c>ApiBaseUrl</c> MSBuild property
/// (<c>dotnet build -p:ApiBaseUrl=https://api.example.com/</c>), stamped into the assembly as metadata. Without it the local
/// development API is used: the Android emulator reaches the host as 10.0.2.2, the iOS simulator and desktop as 127.0.0.1.</summary>
public static class AppConfig
{
    public const int LocalApiPort = 5199;

    public static Uri ApiBaseUrl { get; } = ResolveApiBaseUrl();

    /// <summary>The local database file, in the app's private data folder.</summary>
    public static string DatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "CustomGolfStats", "golf.db");

    private static Uri ResolveApiBaseUrl()
    {
        var configured = typeof(AppConfig).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ApiBaseUrl")?.Value;
        if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured.EndsWith('/') ? configured : configured + "/", UriKind.Absolute, out var uri))
            return uri;

#if __ANDROID__
        const string host = "10.0.2.2";
#else
        const string host = "127.0.0.1";
#endif
        return new Uri($"http://{host}:{LocalApiPort}/");
    }
}
