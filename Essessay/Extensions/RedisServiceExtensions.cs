using Essessay.Options;
using Essessay.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Essessay.Extensions;

public static class RedisServiceExtensions
{
    /// <summary>
    /// The board runs on one instance out of the box. Point ConnectionStrings:Redis at
    /// a server and this connects once, registering the multiplexer so every other
    /// Redis-aware piece — the board store/backplane, the data protection key ring,
    /// the health check below — resolves it from DI instead of each reading
    /// configuration and connecting independently.
    ///
    /// Returns the multiplexer (or null) so the one caller that needs it synchronously
    /// at registration time, <see cref="DataProtectionServiceExtensions.AddEssessayDataProtection"/>,
    /// can take it directly; everything else should resolve <see cref="IConnectionMultiplexer"/>
    /// from the container.
    /// </summary>
    public static IConnectionMultiplexer? AddRedisIfConfigured(this WebApplicationBuilder builder)
    {
        // Bound so IOptions<RedisOptions> is available to anything that wants it, but the
        // decision below has to happen synchronously, now — before the container is built —
        // so it reads the same values directly rather than waiting on that binding.
        builder.Services.AddOptions<RedisOptions>()
            .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
            .PostConfigure(options => options.ConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "");

        // Unconditional: MapHealthChecks("/health", ...) in Program.cs needs this called
        // at least once regardless of whether Redis itself contributes a check to it.
        var health = builder.Services.AddHealthChecks();

        var redisOptions = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        redisOptions.ConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "";

        if (!redisOptions.IsConfigured) return null;

        var connectionOptions = ConfigurationOptions.Parse(redisOptions.ConnectionString);
        connectionOptions.AbortOnConnectFail = false; // start up even if Redis is not there yet
        var multiplexer = ConnectionMultiplexer.Connect(connectionOptions);

        builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        builder.Services.AddSingleton(new BoardRedisOptions(redisOptions.KeyPrefix));

        health.AddCheck("redis", () => multiplexer.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("not connected"));

        return multiplexer;
    }
}
