using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Hosting;

/// <summary>
/// X-Forwarded-For only from configured proxies. The framework's defaults trust loopback, and clearing both lists
/// makes it trust anyone (the SY-07 bypass: a fresh fake XFF per request got a fresh rate-limit bucket), so we
/// replace the lists with exactly the configured ones and skip the middleware entirely when none are configured.
/// </summary>
internal static class ForwardedHeadersSetup
{
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersSettings>().BindConfiguration("ForwardedHeaders").ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ForwardedHeadersSettings>>((o, settings) =>
        {
            var s = settings.Value;
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = s.ForwardLimit;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var proxy in s.KnownProxies) o.KnownProxies.Add(IPAddress.Parse(proxy));
            foreach (var network in s.KnownNetworks) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
        return services;
    }

    public static IApplicationBuilder UseTrustedForwardedHeaders(this IApplicationBuilder app)
    {
        var settings = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;
        return settings.Enabled ? app.UseForwardedHeaders() : app;
    }
}
