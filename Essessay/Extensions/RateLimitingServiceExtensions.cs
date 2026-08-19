using System.Threading.RateLimiting;
using Essessay.Options;
using Essessay.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Essessay.Extensions;

public static class RateLimitingServiceExtensions
{
    /// <summary>
    /// Three things are unbounded without a limiter, and they fail differently.
    ///
    /// A board stream is a connection held open for as long as the client likes, plus a
    /// reader on the backplane. Nothing stopped one client opening thousands.
    ///
    /// The Identity pages hand out work on request: ForgotPassword sends an email to
    /// whoever is named, so an unthrottled form is a way to mail a stranger repeatedly.
    /// Lockout guards one account against guessing; it does nothing about volume.
    ///
    /// Adding, moving and deleting cards grows the board store, which nothing ever
    /// trims — unlike the patch log, which is a bounded ring buffer. An antiforgery
    /// cookie is one GET away, so without this a client could loop card creation
    /// forever, broadcasting to (and growing the board for) everyone else on it.
    /// </summary>
    public static WebApplicationBuilder AddEssessayRateLimiting(this WebApplicationBuilder builder)
    {
        // Bound and validated so a mistyped limit (a stray "-5") fails at startup rather
        // than misbehaving silently; loosened by the test host, tightened by one test to
        // prove each limiter actually runs — see RateLimitTests.
        builder.Services.AddOptions<RateLimitsOptions>()
            .Bind(builder.Configuration.GetSection(RateLimitsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // A limit nobody can see is indistinguishable from an outage, from the outside.
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<EssessayMetrics>()
                    .Rejected(context.HttpContext.GetEndpoint()?.DisplayName ?? "unknown");
                return ValueTask.CompletedTask;
            };

            options.AddPolicy(RateLimits.BoardStream, context =>
            {
                var limits = Limits(context);
                return RateLimitPartition.GetConcurrencyLimiter(ClientKey(context),
                    _ => new ConcurrencyLimiterOptions { PermitLimit = limits.BoardStreamsPerClient, QueueLimit = 0 });
            });

            options.AddPolicy(RateLimits.Identity, context =>
            {
                var limits = Limits(context);
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.IdentityRequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });

            options.AddPolicy(RateLimits.BoardCards, context =>
            {
                var limits = Limits(context);
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.BoardCardsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });

            // Partitioning by address, which is only the real client's once UseForwardedHeaders
            // is on — behind a proxy without it every request shares the proxy's address and
            // the whole deployment rate-limits as if it were one visitor.
            static string ClientKey(HttpContext context) =>
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Read from request services rather than captured once up front, so a policy
            // always sees the options as currently bound instead of a startup-time snapshot.
            static RateLimitsOptions Limits(HttpContext context) =>
                context.RequestServices.GetRequiredService<IOptions<RateLimitsOptions>>().Value;
        });

        return builder;
    }
}
