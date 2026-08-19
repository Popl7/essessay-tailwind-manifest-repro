using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Essessay.Extensions;

public static class HealthCheckEndpointExtensions
{
    /// <summary>
    /// Something for a load balancer and for compose's depends_on to probe. Reports
    /// the Redis connection when there is one, so an instance that lost the backplane
    /// stops claiming to be a healthy member of the deployment.
    ///
    /// Per check rather than the default bare "Healthy": an aggregate tells a prober
    /// whether to route traffic here, but tells whoever is woken up nothing about what
    /// broke. The compose healthcheck greps this body, so its pattern moved with it.
    /// </summary>
    public static WebApplication MapEssessayHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    status = report.Status.ToString(),
                    durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
                    checks = report.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                        description = entry.Value.Description,
                    }),
                });
            },
        }).AllowAnonymous();

        return app;
    }
}
