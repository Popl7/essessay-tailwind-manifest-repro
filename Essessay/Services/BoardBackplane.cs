using Essessay.Models;
using StarFederation.Datastar;

namespace Essessay.Services;

/// <summary>A rendered fragment to broadcast, tagged with the cursor clients resume from.</summary>
public record BoardPatch(string EventId, string Html, string? Selector, ElementPatchMode Mode);

/// <summary>The answer to "what did I miss since <c>cursor</c>?"</summary>
/// <param name="Patches">The patches to send, oldest first.</param>
/// <param name="ResyncRequired">True when the cursor is unknown or too old to catch up from.</param>
/// <param name="Cursor">Where the client stands after applying this.</param>
public record BoardReplay(IReadOnlyList<BoardPatch> Patches, bool ResyncRequired, string Cursor);

/// <summary>Something happened that every instance needs to hear about.</summary>
public abstract record BoardNotification
{
    /// <summary>New patches are available; read from your cursor.</summary>
    public sealed record Patch : BoardNotification;

    /// <summary>The viewer list changed; re-render the presence bar.</summary>
    public sealed record Presence : BoardNotification;

    /// <summary>End this connection's stream, wherever it happens to live.</summary>
    public sealed record Close(string ConnectionId) : BoardNotification;
}

/// <summary>
/// Everything the board needs to share between instances: the patch log clients
/// resume from, who is connected, and the notifications that wake other instances.
/// One implementation keeps it in memory (single instance); the other puts it in
/// Redis (any number of instances).
/// </summary>
public interface IBoardBackplane
{
    /// <summary>Appends a patch to the shared log and tells every instance about it.</summary>
    Task<string> PublishAsync(string html, string? selector, ElementPatchMode mode);

    /// <summary>Reads everything after <paramref name="cursor"/>, or asks for a resync.</summary>
    Task<BoardReplay> ReadSinceAsync(string? cursor);

    /// <summary>The cursor a client stands at once it has the whole board.</summary>
    Task<string> CurrentCursorAsync();

    Task JoinAsync(string connectionId, string name);
    Task LeaveAsync(string connectionId);
    Task RenameAsync(string connectionId, string name);
    Task MarkTypingAsync(string connectionId);

    /// <summary>Keeps this instance's connections from being pruned as stale.</summary>
    Task HeartbeatAsync(IReadOnlyCollection<string> connectionIds);

    Task<IReadOnlyList<BoardViewer>> ViewersAsync();

    /// <summary>
    /// The single viewer with this connection id, or null if it isn't (or is no longer)
    /// here. For the caller that only needs one — asking ViewersAsync for it means
    /// reading and deserializing every viewer just to throw away all but one.
    /// </summary>
    Task<BoardViewer?> ViewerAsync(string connectionId);

    /// <summary>Asks whichever instance owns the connection to end its stream.</summary>
    Task RequestCloseAsync(string connectionId);

    /// <summary>Raised on every instance, including the one that caused it.</summary>
    event Action<BoardNotification>? Notified;
}
