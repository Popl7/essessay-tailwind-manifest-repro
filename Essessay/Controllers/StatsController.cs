using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Models;
using Essessay.Services;

namespace Essessay.Controllers;

// Polled by data-on-interval. Every call sends two SSE events: an element patch
// (morphs #stats) and a signal patch (drives data-style and data-on-signal-patch).
[ApiController]
[Route("api/[controller]")]
public class StatsController(IDatastarService datastar, IPartialRenderer renderer, IAttemptCounter attempts) : ControllerBase
{
    [HttpGet]
    public async Task Get()
    {
        var requests = attempts.Next("stats");
        var load = Random.Shared.Next(5, 96);

        var vm = new StatsViewModel(requests, load, DateTimeOffset.Now);
        var html = await renderer.RenderAsync("_Stats", vm, ControllerContext);
        await datastar.PatchElementsAsync(html);

        await datastar.PatchSignalsAsync(new { stats = new { requests, load } });
    }
}
