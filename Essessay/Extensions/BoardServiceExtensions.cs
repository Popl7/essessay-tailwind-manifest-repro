using Essessay.Services;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace Essessay.Extensions;

public static class BoardServiceExtensions
{
    /// <summary>
    /// The shared board. Store and backplane are registered as factories that check for
    /// an <see cref="IConnectionMultiplexer"/> at resolve time rather than branching here
    /// on configuration — <see cref="RedisServiceExtensions.AddRedisIfConfigured"/> is what
    /// decides whether one exists, so the board itself only needs to ask.
    /// </summary>
    public static WebApplicationBuilder AddBoard(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IBoardStore>(sp =>
            sp.GetService<IConnectionMultiplexer>() is { } redis
                ? new RedisBoardStore(redis, sp.GetRequiredService<BoardRedisOptions>())
                : new InMemoryBoardStore());

        builder.Services.AddSingleton<IBoardBackplane>(sp =>
            sp.GetService<IConnectionMultiplexer>() is { } redis
                ? new RedisBoardBackplane(redis, sp.GetRequiredService<BoardRedisOptions>(), sp.GetRequiredService<IMemoryCache>())
                : new InMemoryBoardBackplane());

        builder.Services.AddSingleton<IBoardConnections, BoardConnections>();
        builder.Services.AddSingleton<EssessayMetrics>();

        // Used to cache the rendered presence bar — identical for every connection on a
        // given heartbeat, see BoardController.
        builder.Services.AddMemoryCache();

        return builder;
    }
}
