using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Models;
using Essessay.Services;

namespace Essessay.Controllers;

// Endpoints that fail (or stall) on purpose, so the client can show the
// `datastar-fetch` lifecycle events, the retry options and request cancellation.
[ApiController]
[Route("api/flaky")]
public class FlakyController(IDatastarService datastar, IPartialRenderer renderer, IAttemptCounter attempts) : ControllerBase
{
    private const int SlowDelayMs = 1500;

    // ?fail=true returns a bare 500 with no SSE body. Datastar surfaces that as a
    // `datastar-fetch` event of type `error` carrying detail.argsRaw.status.
    [HttpGet]
    public async Task Get(bool fail = false)
    {
        var attempt = attempts.Next("flaky");
        if (fail)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        await PatchResultAsync("/api/flaky", attempt);
    }

    // Fails the first two attempts of every three. With {retry: 'error'} the client
    // keeps re-opening the request until this succeeds on the third try.
    [HttpGet("unstable")]
    public async Task Unstable()
    {
        var attempt = attempts.Next("unstable");
        if (attempt % 3 != 0)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        await PatchResultAsync("/api/flaky/unstable", attempt);
    }

    // Takes 1.5s, then appends one line to #slow-log. Aborted requests (the default
    // requestCancellation: 'auto') cancel the token and never write a line.
    [HttpGet("slow")]
    public async Task Slow(string mode, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            await Task.Delay(SlowDelayMs, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return; // client cancelled the request
        }

        var vm = new SlowRequestViewModel(mode, startedAt, DateTimeOffset.Now);
        var html = await renderer.RenderAsync("_SlowLine", vm, ControllerContext);
        await datastar.PatchElementsAsync(html, new PatchElementsOptions
        {
            Selector = "#slow-log",
            PatchMode = ElementPatchMode.Append
        });
    }

    private async Task PatchResultAsync(string label, int attempt)
    {
        var vm = new FlakyViewModel(label, attempt, DateTimeOffset.Now);
        var html = await renderer.RenderAsync("_FlakyResult", vm, ControllerContext);
        await datastar.PatchElementsAsync(html, new PatchElementsOptions { UseViewTransition = true });
    }
}
