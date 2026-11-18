using Microsoft.AspNetCore.HttpOverrides;

namespace CustomGolfStats.Api.Infrastructure;

/// <summary>
/// The deployed API sits behind Caddy (deploy/compose.yaml), which terminates TLS and adds <c>X-Forwarded-For</c> and
/// <c>X-Forwarded-Proto</c>. Those headers are honoured only from the networks in <c>ReverseProxy:KnownNetworks</c>
/// (comma-separated CIDRs, the Compose proxy network) and loopback; from anywhere else they are ignored, so a client
/// can't forge its address or scheme. Unset, only loopback is trusted.
/// </summary>
public static class ReverseProxy
{
    public const string KnownNetworksSetting = "ReverseProxy:KnownNetworks";

    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Caddy is the only proxy, so only the last hop it appends is used.
            options.ForwardLimit = 1;
            foreach (var cidr in (configuration[KnownNetworksSetting] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
            }
        });
        return services;
    }
}
