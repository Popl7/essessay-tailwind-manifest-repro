using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace Essessay.Extensions;

public static class DataProtectionServiceExtensions
{
    /// <summary>
    /// Every instance derives its antiforgery tokens and auth cookies from the data
    /// protection key ring. Left alone, each one generates its own, so a load balancer
    /// sending the next request elsewhere gets a 400 on every POST and logs the user
    /// out — the key ring has to be as shared as the board is.
    /// </summary>
    /// <param name="redis">
    /// The multiplexer from <see cref="RedisServiceExtensions.AddRedisIfConfigured"/>, or
    /// null on a single instance. Taken directly rather than resolved from DI because
    /// <c>PersistKeysToStackExchangeRedis</c> needs the concrete connection now, not a
    /// lazily-resolved one.
    /// </param>
    public static WebApplicationBuilder AddEssessayDataProtection(this WebApplicationBuilder builder, IConnectionMultiplexer? redis)
    {
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Essessay");

        if (redis is not null)
        {
            var keyPrefix = builder.Configuration["Redis:KeyPrefix"] ?? "";

            // Same switch as the board: point at Redis and the instances become one
            // deployment, here by sharing the keys instead of the patch log.
            dataProtection.PersistKeysToStackExchangeRedis(redis, $"{keyPrefix}dataprotection:keys");
        }

        return builder;
    }
}
