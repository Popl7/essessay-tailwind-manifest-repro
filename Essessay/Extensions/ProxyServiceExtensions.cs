using Microsoft.AspNetCore.HttpOverrides;

namespace Essessay.Extensions;

public static class ProxyServiceExtensions
{
    /// <summary>
    /// Behind a TLS-terminating proxy the request arrives as plain HTTP on an internal
    /// address, so Request.Scheme is "http" — and Identity builds its confirmation and
    /// password-reset links from Request.Scheme. Without this the only link a new account
    /// ever receives points at the container over HTTP, which is to say the deployment
    /// compose.yaml documents cannot register anyone.
    ///
    /// Off unless configured, because trusting these headers from a client that is not a
    /// proxy lets it dictate the scheme and its own apparent IP. Deployments that put
    /// something in front set TrustProxyHeaders; a direct `dotnet run` does not.
    ///
    /// A flat, top-level key rather than its own options section: it's a single flag with
    /// nothing to validate, and it's also compose.yaml's and the README's documented name
    /// for it — nothing to gain by renaming it into a nested one.
    /// </summary>
    public static WebApplicationBuilder AddProxyForwarding(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue("TrustProxyHeaders", false)) return builder;

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Not XForwardedHost: Request.Host feeds those same links, so honouring a
            // forwarded host would let a poisoned header mail somebody a password-reset
            // link pointing at another domain. The proxy's own Host header is enough.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;

            // The defaults trust loopback only, and a sibling container is not loopback.
            // Safe here precisely because the whole block is opt-in.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return builder;
    }

    /// <summary>
    /// First in the pipeline when enabled, so everything after it — HTTPS redirection,
    /// the rate limiter's view of who the client is, the links Identity builds — sees
    /// the outside world's scheme and address rather than the proxy's.
    /// </summary>
    public static WebApplication UseProxyForwardingIfConfigured(this WebApplication app)
    {
        if (app.Configuration.GetValue("TrustProxyHeaders", false))
        {
            app.UseForwardedHeaders();
        }

        return app;
    }
}
