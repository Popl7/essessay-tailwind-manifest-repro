using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Models;
using Essessay.Services;

namespace Essessay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClockController(IDatastarService datastar, IPartialRenderer renderer) : ControllerBase
{
    // A long-lived SSE stream: holds the connection open and pushes one patch
    // per second. The client renders each fragment into #clock as it arrives.
    [HttpGet]
    public async Task Get(CancellationToken cancellationToken)
    {
        const int totalTicks = 20;
        for (var tick = 1; tick <= totalTicks && !cancellationToken.IsCancellationRequested; tick++)
        {
            var vm = new ClockViewModel(DateTimeOffset.Now, tick * 100 / totalTicks, tick == totalTicks);
            var html = await renderer.RenderAsync("_Clock", vm, ControllerContext);
            await datastar.PatchElementsAsync(html, new PatchElementsOptions { UseViewTransition = true });

            if (tick < totalTicks)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break; // client disconnected
                }
            }
        }
    }
}
