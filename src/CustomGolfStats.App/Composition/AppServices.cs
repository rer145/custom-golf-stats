using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.ApiAccess;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Sync;

namespace CustomGolfStats.App.Composition;

/// <summary>
/// The composition root. Singletons: clock, platform services (secure store, pedometer, location, connectivity, see
/// Platforms/*/PlatformModule.cs), the open <see cref="LocalDb"/>, the API session (authenticated client + <see cref="TokenService"/>),
/// <see cref="SyncEngine"/>, <see cref="AccountSwitchService"/> and the <see cref="INavigator"/>. View models are transient: each
/// page navigation gets a fresh one.
/// </summary>
public static class AppServices
{
    public static ServiceProvider Build(LocalDb db, INavigator navigator, Uri apiBaseUrl)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IClock, SystemClock>();
        PlatformModule.AddPlatformServices(services);

        services.AddSingleton(db);
        services.AddSingleton(navigator);
        services.AddSingleton(sp => ApiClientFactory.Create(apiBaseUrl, sp.GetRequiredService<ISecureStore>()));
        services.AddSingleton(sp => sp.GetRequiredService<ApiSession>().Tokens);
        services.AddSingleton<ICustomGolfStatsApiClient>(sp => sp.GetRequiredService<ApiSession>().Client);
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<AccountSwitchService>();

        services.AddTransient<SignInViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<CoursePickerViewModel>();
        services.AddTransient<ManualCourseViewModel>();
        services.AddTransient<StartRoundViewModel>();
        services.AddTransient<RoundViewModel>();
        services.AddTransient<QuickHoleViewModel>();
        services.AddTransient<ShotEntryViewModel>();
        services.AddTransient<RoundSummaryViewModel>();
        services.AddTransient<ScorecardViewModel>();
        services.AddTransient<OverallStatsViewModel>();
        services.AddTransient<ProfileViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
