using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Essessay.Services;

/// <summary>
/// One client's stream, as seen by the instance serving it. It holds no patches:
/// the shared log does that, and this is only the nudge that says "go and read it".
/// </summary>
public sealed class BoardConnection
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public required string ConnectionId { get; init; }
    public bool Closed { get; private set; }

    /// <summary>Where this client stands in the shared patch log.</summary>
    public string? Cursor { get; set; }

    /// <summary>Set when the presence bar needs re-rendering for this client.</summary>
    public bool PresenceChanged { get; set; } = true;

    internal void Wake()
    {
        // A single pending nudge is enough: the loop reads everything available.
        try
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Raced with another waker; the loop is about to run anyway.
        }
    }

    internal void Close()
    {
        Closed = true;
        Wake();
    }

    /// <summary>Waits for a nudge, or for the heartbeat to fall due.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _wake.WaitAsync(timeout, cancellationToken);
}

/// <summary>The streams this instance is serving. Everything shared lives in the backplane.</summary>
public interface IBoardConnections
{
    BoardConnection Add(string connectionId);
    void Remove(BoardConnection connection);
    IReadOnlyCollection<string> ConnectionIds { get; }

    /// <summary>
    /// Bumped every time presence actually changes — see BoardController.RenderPresenceAsync,
    /// which uses this to stop a slow render from overwriting a fresher cache eviction with
    /// a stale one.
    /// </summary>
    long PresenceGeneration { get; }
}

public class BoardConnections : IBoardConnections, IDisposable
{
    /// <summary>
    /// The rendered presence bar is identical for every connection — see
    /// BoardController.RenderPresenceAsync — so it is cached under this key rather
    /// than re-rendered once per connection per heartbeat.
    /// </summary>
    public const string PresenceCacheKey = "board:presence-html";

    private readonly ConcurrentDictionary<string, BoardConnection> _connections = new();
    private readonly IBoardBackplane _backplane;
    private readonly IMemoryCache _cache;
    private long _presenceGeneration;

    public BoardConnections(IBoardBackplane backplane, IMemoryCache cache)
    {
        _backplane = backplane;
        _cache = cache;
        _backplane.Notified += OnNotified;
    }

    public IReadOnlyCollection<string> ConnectionIds => _connections.Keys.ToList();

    public long PresenceGeneration => Interlocked.Read(ref _presenceGeneration);

    public BoardConnection Add(string connectionId)
    {
        var connection = new BoardConnection { ConnectionId = connectionId };
        _connections[connectionId] = connection;
        return connection;
    }

    public void Remove(BoardConnection connection) => _connections.TryRemove(connection.ConnectionId, out _);

    public void Dispose() => _backplane.Notified -= OnNotified;

    /// <summary>
    /// Fired for anything that happened on any instance. Local connections are only
    /// nudged; each one then reads the shared log from its own cursor.
    /// </summary>
    private void OnNotified(BoardNotification notification)
    {
        switch (notification)
        {
            case BoardNotification.Close close:
                if (_connections.TryGetValue(close.ConnectionId, out var target)) target.Close();
                break;

            case BoardNotification.Presence:
                // Stale the moment presence actually changes, so a join or rename
                // shows up on the next render rather than waiting out the cache's
                // own TTL (which exists only to catch a typing marker lapsing with
                // no notification at all — see BoardController). The generation
                // bump is what stops a render that was already in flight against
                // Redis from landing after this eviction and re-caching a snapshot
                // taken before the change — BoardController checks it before it
                // trusts its own read enough to cache it.
                Interlocked.Increment(ref _presenceGeneration);
                _cache.Remove(PresenceCacheKey);
                foreach (var connection in _connections.Values)
                {
                    connection.PresenceChanged = true;
                    connection.Wake();
                }
                break;

            default:
                foreach (var connection in _connections.Values) connection.Wake();
                break;
        }
    }
}
