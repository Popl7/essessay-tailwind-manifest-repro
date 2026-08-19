using System.Collections.Concurrent;
using Essessay.Models;
using StarFederation.Datastar;

namespace Essessay.Services;

/// <summary>
/// The single-instance backplane: a ring buffer of patches and a dictionary of
/// viewers. Used whenever no Redis connection string is configured, so the app
/// still runs with nothing but the .NET SDK installed.
/// </summary>
public class InMemoryBoardBackplane : IBoardBackplane
{
    // How many patches a reconnecting client can be behind before it needs a resync.
    private const int LogSize = 100;
    private static readonly TimeSpan TypingWindow = TimeSpan.FromSeconds(3);

    private readonly object _lock = new();
    private readonly Queue<BoardPatch> _log = new();
    private readonly ConcurrentDictionary<string, Presence> _viewers = new();
    private long _lastEventId;

    public event Action<BoardNotification>? Notified;

    private sealed record Presence(string Name, DateTimeOffset TypingUntil);

    public Task<string> PublishAsync(string html, string? selector, ElementPatchMode mode)
    {
        BoardPatch patch;
        lock (_lock)
        {
            patch = new BoardPatch((++_lastEventId).ToString(), html, selector, mode);
            _log.Enqueue(patch);
            while (_log.Count > LogSize) _log.Dequeue();
        }

        Notified?.Invoke(new BoardNotification.Patch());
        return Task.FromResult(patch.EventId);
    }

    public Task<BoardReplay> ReadSinceAsync(string? cursor)
    {
        lock (_lock)
        {
            var current = _lastEventId.ToString();

            if (cursor is null || !long.TryParse(cursor, out var since) || since > _lastEventId)
            {
                return Task.FromResult(new BoardReplay([], true, current));
            }

            // Everything still in the log has to be reachable from the cursor,
            // otherwise the client would silently skip a patch.
            var oldest = _log.Count == 0 ? _lastEventId : long.Parse(_log.Peek().EventId) - 1;
            if (since < oldest) return Task.FromResult(new BoardReplay([], true, current));

            var patches = _log.Where(p => long.Parse(p.EventId) > since).ToList();
            var newCursor = patches.Count == 0 ? cursor : patches[^1].EventId;
            return Task.FromResult(new BoardReplay(patches, false, newCursor));
        }
    }

    public Task<string> CurrentCursorAsync()
    {
        lock (_lock) return Task.FromResult(_lastEventId.ToString());
    }

    public Task JoinAsync(string connectionId, string name)
    {
        _viewers[connectionId] = new Presence(name, DateTimeOffset.MinValue);
        return NotifyPresenceAsync();
    }

    public Task LeaveAsync(string connectionId)
    {
        _viewers.TryRemove(connectionId, out _);
        return NotifyPresenceAsync();
    }

    public Task RenameAsync(string connectionId, string name)
    {
        if (_viewers.TryGetValue(connectionId, out var existing))
        {
            _viewers[connectionId] = existing with { Name = name };
        }
        return NotifyPresenceAsync();
    }

    public Task MarkTypingAsync(string connectionId)
    {
        if (_viewers.TryGetValue(connectionId, out var existing))
        {
            _viewers[connectionId] = existing with { TypingUntil = DateTimeOffset.Now.Add(TypingWindow) };
        }
        return NotifyPresenceAsync();
    }

    // Nothing expires in a single process: a connection is gone the moment it leaves.
    public Task HeartbeatAsync(IReadOnlyCollection<string> connectionIds) => Task.CompletedTask;

    public Task<IReadOnlyList<BoardViewer>> ViewersAsync()
    {
        var now = DateTimeOffset.Now;
        IReadOnlyList<BoardViewer> viewers = _viewers
            .Select(entry => new BoardViewer(entry.Key, entry.Value.Name, entry.Value.TypingUntil > now))
            .OrderBy(viewer => viewer.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(viewers);
    }

    public Task<BoardViewer?> ViewerAsync(string connectionId)
    {
        if (!_viewers.TryGetValue(connectionId, out var presence)) return Task.FromResult<BoardViewer?>(null);

        var isTyping = presence.TypingUntil > DateTimeOffset.Now;
        return Task.FromResult<BoardViewer?>(new BoardViewer(connectionId, presence.Name, isTyping));
    }

    public Task RequestCloseAsync(string connectionId)
    {
        Notified?.Invoke(new BoardNotification.Close(connectionId));
        return Task.CompletedTask;
    }

    private Task NotifyPresenceAsync()
    {
        Notified?.Invoke(new BoardNotification.Presence());
        return Task.CompletedTask;
    }
}
