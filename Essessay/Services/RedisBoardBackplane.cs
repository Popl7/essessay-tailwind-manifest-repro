using System.Text.Json;
using Essessay.Models;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using StarFederation.Datastar;

namespace Essessay.Services;

/// <summary>
/// The multi-instance backplane. The patch log is a Redis stream, so a stream id
/// *is* the cursor a client resumes from — which is exactly the shape of SSE's
/// last-event-id. Instances hear about each other over one pub/sub channel.
/// </summary>
public class RedisBoardBackplane : IBoardBackplane, IDisposable
{
    private const int LogSize = 100;
    private static readonly TimeSpan TypingWindow = TimeSpan.FromSeconds(3);

    /// <summary>How long a viewer survives without a heartbeat — four missed beats.</summary>
    private static readonly TimeSpan ViewerLifetime = TimeSpan.FromSeconds(12);

    /// <summary>The id of an empty stream: everything is "after" it.</summary>
    private const string Origin = "0-0";

    /// <summary>
    /// Nearly every connection wakes on the same Patch notification asking the same
    /// question — "what's after my cursor" — and almost always with the same cursor,
    /// since most of them were fully caught up before whatever just got published. One
    /// cache slot, not one per cursor ever seen: the cursor most of them share changes
    /// together, so there's rarely more than one worth keeping at a time.
    /// </summary>
    private const string ReadSinceCacheKey = "board:read-since";

    private readonly IConnectionMultiplexer _redis;
    private readonly IMemoryCache _cache;
    private readonly RedisKey _events;
    private readonly RedisKey _added;
    private readonly RedisKey _viewers;
    private readonly RedisChannel _channel;
    private readonly ISubscriber _subscriber;
    private long _patchGeneration;

    public event Action<BoardNotification>? Notified;

    private sealed record Presence(string Name, long TypingUntil, long ExpiresAt);

    public RedisBoardBackplane(IConnectionMultiplexer redis, BoardRedisOptions options, IMemoryCache cache)
    {
        _redis = redis;
        _cache = cache;
        _events = options.Prefix + "board:events";
        _added = options.Prefix + "board:events:added";
        _viewers = options.Prefix + "board:viewers";
        _channel = RedisChannel.Literal(options.Prefix + "board:notify");

        _subscriber = redis.GetSubscriber();
        _subscriber.Subscribe(_channel, (_, message) =>
        {
            var notification = Parse(message!);

            // Unlike presence, nothing ever lapses here on its own — every mutation to
            // the log is always paired with exactly this notification, so there's no
            // silent-decay case to also guard with a TTL. The generation bump is what
            // stops a slow read that started before this notification from landing
            // after and re-caching an answer that's missing whatever was just
            // published — see ReadSinceAsync.
            if (notification is BoardNotification.Patch)
            {
                Interlocked.Increment(ref _patchGeneration);
                _cache.Remove(ReadSinceCacheKey);
            }

            Notified?.Invoke(notification);
        });
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<string> PublishAsync(string html, string? selector, ElementPatchMode mode)
    {
        var id = await Db.StreamAddAsync(_events,
        [
            new NameValueEntry("html", html),
            new NameValueEntry("selector", selector ?? ""),
            new NameValueEntry("mode", mode.ToString())
        ], maxLength: LogSize, useApproximateMaxLength: true);

        // Counting what we have ever added is how a reader tells "the log was
        // trimmed" from "the log is simply short" — see ReadSinceAsync.
        await Db.StringIncrementAsync(_added);

        // Redis delivers this to every subscriber including us, so the local
        // connections are woken by the same path as the remote ones.
        await _subscriber.PublishAsync(_channel, "patch");
        return id!;
    }

    /// <summary>
    /// A null cursor (a brand-new connection) always resolves to a resync anyway — one
    /// cheap CurrentCursorAsync call, and the expensive part of a whole-board render is
    /// already cached separately in BoardController — so only a real cursor is worth
    /// memoizing here. See ReadSinceCacheKey for why one slot is enough.
    /// </summary>
    public async Task<BoardReplay> ReadSinceAsync(string? cursor)
    {
        if (cursor is not null &&
            _cache.TryGetValue(ReadSinceCacheKey, out (string Cursor, BoardReplay Replay) cached) &&
            cached.Cursor == cursor)
        {
            return cached.Replay;
        }

        // Stamped before the reads, not after — the same reasoning as the presence
        // cache's generation guard: a read that started before a Patch notification can
        // still finish after it evicts the cache, and without this check that stale
        // result would win the race and get cached anyway, this time with nothing to
        // ever correct it until the next unrelated patch happens to arrive.
        var generation = Interlocked.Read(ref _patchGeneration);
        var replay = await ReadSinceUncachedAsync(cursor);

        if (cursor is not null && Interlocked.Read(ref _patchGeneration) == generation)
        {
            _cache.Set(ReadSinceCacheKey, (cursor, replay));
        }

        return replay;
    }

    private async Task<BoardReplay> ReadSinceUncachedAsync(string? cursor)
    {
        var current = await CurrentCursorAsync();

        if (cursor is null) return new BoardReplay([], true, current);
        if (cursor == current) return new BoardReplay([], false, cursor);

        // A cursor beyond our newest entry means the log was reset under the client.
        if (CompareIds(cursor, current) > 0) return new BoardReplay([], true, current);

        var oldest = (await Db.StreamRangeAsync(_events, count: 1)).FirstOrDefault();
        if (oldest.Id.IsNull) return new BoardReplay([], true, current);

        // Sitting before the first surviving entry is only a problem if entries were
        // actually dropped. A client that connected to an empty board sits at 0-0 and
        // is perfectly able to catch up on everything since.
        if (CompareIds(cursor, oldest.Id!) < 0 && await WasTrimmedAsync())
        {
            return new BoardReplay([], true, current);
        }

        // StreamRead is exclusive of the cursor, which is what a resume wants.
        var entries = await Db.StreamReadAsync(_events, cursor, LogSize);
        var patches = entries.Select(ToPatch).ToList();

        return new BoardReplay(patches, false, patches.Count == 0 ? cursor : patches[^1].EventId);
    }

    public async Task<string> CurrentCursorAsync()
    {
        var newest = (await Db.StreamRangeAsync(_events, count: 1, messageOrder: Order.Descending)).FirstOrDefault();
        return newest.Id.IsNull ? Origin : newest.Id!;
    }

    public Task JoinAsync(string connectionId, string name) =>
        WritePresenceAsync(connectionId, new Presence(name, 0, Expiry()));

    public async Task LeaveAsync(string connectionId)
    {
        await Db.HashDeleteAsync(_viewers, connectionId);
        await NotifyAsync("presence");
    }

    public async Task RenameAsync(string connectionId, string name)
    {
        var existing = await ReadPresenceAsync(connectionId);
        if (existing is null) return;

        await WritePresenceAsync(connectionId, existing with { Name = name, ExpiresAt = Expiry() });
    }

    public async Task MarkTypingAsync(string connectionId)
    {
        var existing = await ReadPresenceAsync(connectionId);
        if (existing is null) return;

        var typingUntil = DateTimeOffset.UtcNow.Add(TypingWindow).ToUnixTimeMilliseconds();
        await WritePresenceAsync(connectionId, existing with { TypingUntil = typingUntil, ExpiresAt = Expiry() });
    }

    /// <summary>
    /// Re-stamps this instance's connections. An instance that dies stops stamping,
    /// and its viewers drop off the list on the next read.
    ///
    /// One HMGET and one HMSET for the whole batch, not a sequential HGET+HSET per
    /// id: every connection this instance is serving calls in on its own timeout, so
    /// an unbatched version costs two round trips per id per call — O(connections²)
    /// Redis round trips per heartbeat interval, not O(connections).
    /// </summary>
    public async Task HeartbeatAsync(IReadOnlyCollection<string> connectionIds)
    {
        if (connectionIds.Count == 0) return;

        var ids = connectionIds.ToArray();
        var fields = Array.ConvertAll(ids, id => (RedisValue)id);
        var raw = await Db.HashGetAsync(_viewers, fields);

        var expiry = Expiry();
        var updates = new List<HashEntry>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            var existing = Deserialize(raw[i]);
            if (existing is not null)
            {
                updates.Add(new HashEntry(ids[i], JsonSerializer.Serialize(existing with { ExpiresAt = expiry })));
            }
        }

        if (updates.Count > 0) await Db.HashSetAsync(_viewers, [.. updates]);
    }

    public async Task<IReadOnlyList<BoardViewer>> ViewersAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var viewers = new List<BoardViewer>();
        var stale = new List<RedisValue>();

        foreach (var entry in await Db.HashGetAllAsync(_viewers))
        {
            var presence = Deserialize(entry.Value);
            if (presence is null || presence.ExpiresAt <= now)
            {
                stale.Add(entry.Name);
                continue;
            }

            viewers.Add(new BoardViewer(entry.Name!, presence.Name, presence.TypingUntil > now));
        }

        // Sweep whatever a dead instance left behind.
        if (stale.Count > 0) await Db.HashDeleteAsync(_viewers, [.. stale]);

        return viewers.OrderBy(viewer => viewer.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<BoardViewer?> ViewerAsync(string connectionId)
    {
        var presence = await ReadPresenceAsync(connectionId);
        if (presence is null) return null;

        // Stale is not swept here — a single read isn't the place to also write.
        // ViewersAsync already sweeps on its own regular cadence.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (presence.ExpiresAt <= now) return null;

        return new BoardViewer(connectionId, presence.Name, presence.TypingUntil > now);
    }

    public Task RequestCloseAsync(string connectionId) => NotifyAsync($"close:{connectionId}");

    /// <summary>True once the log has dropped entries a late reader might still want.</summary>
    private async Task<bool> WasTrimmedAsync()
    {
        var added = (long)await Db.StringGetAsync(_added);
        return added > await Db.StreamLengthAsync(_events);
    }

    public void Dispose() => _subscriber.Unsubscribe(_channel);

    private static BoardNotification Parse(string message) => message switch
    {
        "presence" => new BoardNotification.Presence(),
        var close when close.StartsWith("close:", StringComparison.Ordinal) =>
            new BoardNotification.Close(close["close:".Length..]),
        _ => new BoardNotification.Patch()
    };

    private static BoardPatch ToPatch(StreamEntry entry)
    {
        var fields = entry.Values.ToDictionary(value => value.Name.ToString(), value => value.Value.ToString());
        var selector = fields.GetValueOrDefault("selector");

        return new BoardPatch(
            entry.Id!,
            fields.GetValueOrDefault("html") ?? "",
            string.IsNullOrEmpty(selector) ? null : selector,
            Enum.Parse<ElementPatchMode>(fields.GetValueOrDefault("mode") ?? nameof(ElementPatchMode.Outer)));
    }

    /// <summary>Stream ids sort as two numbers, "&lt;milliseconds&gt;-&lt;sequence&gt;".</summary>
    private static int CompareIds(string left, string right)
    {
        var (leftMs, leftSeq) = SplitId(left);
        var (rightMs, rightSeq) = SplitId(right);

        return leftMs != rightMs ? leftMs.CompareTo(rightMs) : leftSeq.CompareTo(rightSeq);
    }

    private static (long Milliseconds, long Sequence) SplitId(string id)
    {
        var dash = id.IndexOf('-');
        if (dash < 0) return (long.TryParse(id, out var only) ? only : 0, 0);

        _ = long.TryParse(id[..dash], out var milliseconds);
        _ = long.TryParse(id[(dash + 1)..], out var sequence);
        return (milliseconds, sequence);
    }

    private static long Expiry() => DateTimeOffset.UtcNow.Add(ViewerLifetime).ToUnixTimeMilliseconds();

    private static Presence? Deserialize(RedisValue value) =>
        value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Presence>(value.ToString());

    private async Task<Presence?> ReadPresenceAsync(string connectionId) =>
        Deserialize(await Db.HashGetAsync(_viewers, connectionId));

    private async Task WritePresenceAsync(string connectionId, Presence presence)
    {
        await Db.HashSetAsync(_viewers, connectionId, JsonSerializer.Serialize(presence));
        await NotifyAsync("presence");
    }

    private async Task NotifyAsync(string message) => await _subscriber.PublishAsync(_channel, message);
}

/// <summary>Lets tests (and side-by-side instances) keep their keys apart.</summary>
public record BoardRedisOptions(string Prefix = "");
