using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;
using StarFederation.Datastar.ModelBinding;
using Essessay.Models;
using Essessay.Services;

namespace Essessay.Controllers;

// A board shared by every connected client. Each client holds one long-lived SSE
// connection (`stream`); mutations are ordinary requests that render a fragment
// once and append it to the shared patch log, which every instance reads from.
// No client ever patches its own DOM, and no instance needs to know about the
// others beyond that log — see IBoardBackplane.
[ApiController]
[Route("api/board")]
public class BoardController(
    IDatastarService datastar,
    IPartialRenderer renderer,
    IBoardStore store,
    IBoardBackplane backplane,
    IBoardConnections connections,
    IAttemptCounter attempts,
    EssessayMetrics metrics,
    IMemoryCache cache) : ControllerBase
{
    // Long enough to stay quiet, short enough to expire a stale "typing…" flag
    // and to keep this instance's viewers alive in the shared presence list.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(3);

    // Every connection on this instance wakes and re-renders presence on the same
    // events (join, leave, rename, a heartbeat falling due), and the output is the
    // same rendered HTML for all of them — see _BoardPresence.cshtml. BoardConnections
    // evicts this the moment presence actually changes, so this TTL only has to catch
    // the one case nothing notifies on: a typing marker lapsing on its own. It stays
    // well under HeartbeatInterval so that still shows up within one beat.
    private static readonly TimeSpan PresenceCacheDuration = TimeSpan.FromSeconds(1);

    // No expiration — see RenderWholeBoardAsync for why none is needed. One entry,
    // a few KB of HTML, replaced (never merely accumulated) on every write.
    private const string WholeBoardCacheKey = "board:whole-html";

    [HttpGet("stream")]
    [EnableRateLimiting(RateLimits.BoardStream)]
    public async Task Stream(CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid().ToString("N")[..8];
        var name = User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity.Name)
            ? User.Identity.Name!
            : $"Guest {attempts.Next("board-guest")}";

        var connection = connections.Add(connectionId);
        connection.Cursor = TryGetLastEventId();

        await backplane.JoinAsync(connectionId, name);
        metrics.StreamOpened();
        try
        {
            // Tell this client who it is; mutations send it back so the server can
            // attribute them without trusting a form field.
            await datastar.PatchSignalsAsync(
                new { me = new { id = connectionId, name } }, cancellationToken);

            while (!cancellationToken.IsCancellationRequested && !connection.Closed)
            {
                await DrainAsync(connection, cancellationToken);

                // Woken by a change on any instance, or by the heartbeat falling due.
                var woken = await connection.WaitAsync(HeartbeatInterval, cancellationToken);
                if (woken) continue;

                await backplane.HeartbeatAsync(connections.ConnectionIds);
                await SendPresenceAsync(cancellationToken);
                await datastar.PatchSignalsAsync(
                    new { board = new { beat = DateTimeOffset.Now.ToString("HH:mm:ss") } }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away.
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The connection died mid-write; nothing useful to do.
        }
        finally
        {
            // In the finally, so a cancelled request decrements too — otherwise the
            // gauge only ever climbs and stops meaning anything.
            metrics.StreamClosed();
            connections.Remove(connection);
            await backplane.LeaveAsync(connectionId);
        }
    }

    [HttpPost("cards")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.BoardCards)]
    public async Task AddCard([FromSignals] BoardSignals? signals)
    {
        var text = signals?.NewCard?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            await datastar.ExecuteScriptAsync("window.showToast('Type a card first', 'error')");
            return;
        }

        if (text.Length > 80) text = text[..80];

        var card = await store.AddAsync(text, await NameOfAsync(signals));
        await BroadcastCardAsync(card, ElementPatchMode.Append);

        // The only thing this response does is clear the caller's input. The card
        // itself arrives over the stream, exactly like it does for everyone else.
        await datastar.PatchSignalsAsync(new { newCard = "" });
    }

    [HttpPatch("cards/{id:int}/move")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.BoardCards)]
    public async Task MoveCard(int id, int direction, [FromSignals] BoardSignals? signals)
    {
        var card = await store.MoveAsync(id, direction, await NameOfAsync(signals));
        if (card is null) return;

        // A card can't be "moved" between two containers in one patch, so send two:
        // drop the old node, then append the re-rendered card to its new column.
        await backplane.PublishAsync(string.Empty, $"#card-{card.Id}", ElementPatchMode.Remove);
        await BroadcastCardAsync(card, ElementPatchMode.Append);
    }

    [HttpDelete("cards/{id:int}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.BoardCards)]
    public async Task DeleteCard(int id)
    {
        if (await store.RemoveAsync(id))
        {
            await backplane.PublishAsync(string.Empty, $"#card-{id}", ElementPatchMode.Remove);
        }
    }

    [HttpPost("name")]
    [ValidateAntiForgeryToken]
    public async Task Rename([FromSignals] BoardSignals? signals)
    {
        var id = signals?.Me?.Id;
        var name = signals?.Rename?.Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return;

        if (name.Length > 24) name = name[..24];
        await backplane.RenameAsync(id, name);

        // Merge into the existing `me` object; `me.id` is left alone.
        await datastar.PatchSignalsAsync(new { me = new { name }, rename = "" });
    }

    // Throttled from the client, so this runs at most once a second per typist.
    [HttpPost("typing")]
    [ValidateAntiForgeryToken]
    public async Task Typing([FromSignals] BoardSignals? signals)
    {
        if (signals?.Me?.Id is { Length: > 0 } id) await backplane.MarkTypingAsync(id);
    }

    // Ends the caller's own stream — which may be held by a different instance, so
    // the request goes through the backplane — then changes the board while it is
    // down, giving the automatic reconnect something to catch up on.
    [HttpPost("drop")]
    [ValidateAntiForgeryToken]
    public async Task Drop([FromSignals] BoardSignals? signals)
    {
        var id = signals?.Me?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;

        // One lookup does both jobs: whether this is still a real viewer, and its name
        // if so — rather than a name lookup and a separate existence check each reading
        // every viewer just to use one.
        var viewer = await backplane.ViewerAsync(id);
        if (viewer is null) return;

        await backplane.RequestCloseAsync(id);
        await backplane.LeaveAsync(id);

        var card = await store.AddAsync($"Added while {viewer.Name} was disconnected", "system");
        await BroadcastCardAsync(card, ElementPatchMode.Append);
    }

    /// <summary>Sends whatever this client is behind on, plus presence when it changed.</summary>
    private async Task DrainAsync(BoardConnection connection, CancellationToken cancellationToken)
    {
        var replay = await backplane.ReadSinceAsync(connection.Cursor);

        metrics.Resumed(replay.ResyncRequired);

        if (replay.ResyncRequired)
        {
            await SendWholeBoardAsync(replay.Cursor, cancellationToken);
        }
        else
        {
            foreach (var patch in replay.Patches) await SendPatchAsync(patch, cancellationToken);
        }

        connection.Cursor = replay.Cursor;

        if (!connection.PresenceChanged) return;

        connection.PresenceChanged = false;
        await SendPresenceAsync(cancellationToken);
    }

    private async Task BroadcastCardAsync(BoardCard card, ElementPatchMode mode)
    {
        var html = await renderer.RenderAsync("_BoardCard", card, ControllerContext);
        await backplane.PublishAsync(html, ColumnSelector(card.Column), mode);
    }

    // None of the stream's patches ask for a view transition. startViewTransition()
    // only runs one at a time, and this stream patches often and in pairs (a move is
    // a remove plus an append), so overlapping transitions would abort each other and
    // fill the console with "Transition was skipped". Use them for one-off swaps
    // instead — see the Get Data and Todos pages.
    private async Task SendPatchAsync(BoardPatch patch, CancellationToken cancellationToken)
    {
        metrics.PatchSent();

        if (patch.Mode == ElementPatchMode.Remove)
        {
            await datastar.RemoveElementAsync(patch.Selector!,
                new RemoveElementOptions { EventId = patch.EventId }, cancellationToken);
            return;
        }

        // Selector is init-only, so pick the shape up front rather than assigning null.
        var options = patch.Selector is null
            ? new PatchElementsOptions
            {
                PatchMode = patch.Mode,
                EventId = patch.EventId
            }
            : new PatchElementsOptions
            {
                Selector = patch.Selector,
                PatchMode = patch.Mode,
                EventId = patch.EventId
            };

        await datastar.PatchElementsAsync(patch.Html, options, cancellationToken);
    }

    private async Task SendWholeBoardAsync(string cursor, CancellationToken cancellationToken)
    {
        var html = await RenderWholeBoardAsync(cursor);

        // Stamp the cursor this render is current as of, so the next reconnect
        // resumes from here rather than replaying the whole log.
        await datastar.PatchElementsAsync(html, new PatchElementsOptions
        {
            EventId = cursor
        }, cancellationToken);
    }

    /// <summary>
    /// A resync — a brand-new connection, a cursor too old for the log, a wave of
    /// reconnects after a restart or a Redis trim — can land many connections on
    /// ResyncRequired at once, all asking DrainAsync for the board as of the same
    /// "current" cursor. Cached by that cursor so they share one render of the whole
    /// store instead of each re-reading and re-rendering it independently.
    ///
    /// No card is ever changed without also publishing a patch that advances the
    /// cursor, so "the board as of cursor X" never changes retroactively — which is
    /// what makes a hit here self-verifying instead of needing a TTL or an eviction
    /// hook the way the presence cache does: it's used only when its stored cursor
    /// matches the one asked for. A render that started for an older cursor and
    /// finishes after a newer one is already cached can only ever overwrite it with
    /// a stale entry tagged with that older cursor — the next reader compares against
    /// the real current cursor and falls through to a fresh render rather than being
    /// served the wrong board.
    /// </summary>
    private async Task<string> RenderWholeBoardAsync(string cursor)
    {
        if (cache.TryGetValue(WholeBoardCacheKey, out (string Cursor, string Html) cached) && cached.Cursor == cursor)
        {
            return cached.Html;
        }

        var html = await renderer.RenderAsync("_Board", await store.AllAsync(), ControllerContext);
        cache.Set(WholeBoardCacheKey, (cursor, html));
        return html;
    }

    private async Task SendPresenceAsync(CancellationToken cancellationToken)
    {
        var html = await RenderPresenceAsync();

        // Replace, not morph: there is no client state inside the presence bar to
        // preserve. No event id either — presence is never replayed.
        await datastar.PatchElementsAsync(html, new PatchElementsOptions
        {
            PatchMode = ElementPatchMode.Replace
        }, cancellationToken);
    }

    /// <summary>
    /// Every connection calls this on the same triggers and gets the same HTML back —
    /// the partial doesn't know which connection is reading it, "you" is resolved on
    /// the client from $me.id. A join, rename or heartbeat wakes every connection on
    /// this instance at once, so without the cache N connections would independently
    /// read the viewer list and render the identical fragment N times.
    /// </summary>
    private async Task<string> RenderPresenceAsync()
    {
        if (cache.TryGetValue(BoardConnections.PresenceCacheKey, out string? cached)) return cached!;

        // Stamped before the read, not after: a render that started before a Presence
        // notification can still finish after it evicts the cache, and without this
        // check that stale result would win the race and overwrite the eviction —
        // hiding whatever just changed until the TTL alone happens to catch it.
        var generation = connections.PresenceGeneration;
        var html = await renderer.RenderAsync("_BoardPresence",
            new BoardPresenceViewModel(await backplane.ViewersAsync()), ControllerContext);

        if (connections.PresenceGeneration == generation)
        {
            cache.Set(BoardConnections.PresenceCacheKey, html, PresenceCacheDuration);
        }

        return html;
    }

    private string? TryGetLastEventId() =>
        Request.Headers.TryGetValue("last-event-id", out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.ToString()
            : null;

    /// <summary>The shared name for the caller's connection — not the name it claims to have.</summary>
    private async Task<string> NameOfAsync(BoardSignals? signals)
    {
        var id = signals?.Me?.Id;
        if (string.IsNullOrWhiteSpace(id)) return "someone";

        return (await backplane.ViewerAsync(id))?.Name ?? "someone";
    }

    private static string ColumnSelector(BoardColumn column) => $"#col-{column.ToString().ToLowerInvariant()}-cards";
}
