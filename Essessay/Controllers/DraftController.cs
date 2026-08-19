using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar.DependencyInjection;
using StarFederation.Datastar.ModelBinding;
using Essessay.Models;
using Essessay.Services;

namespace Essessay.Controllers;

[ApiController]
[Route("api/draft")]
public class DraftController(IDatastarService datastar, IPartialRenderer renderer) : ControllerBase
{
    // @put with {filterSignals: {include: /^draft\./}} — echo back the exact JSON
    // body we received so the filter (and the default `_`-prefix exclusion) is visible.
    [HttpPut]
    [ValidateAntiForgeryToken]
    public async Task Put()
    {
        var raw = await datastar.ReadSignalsAsync();

        await datastar.PatchSignalsAsync(new
        {
            received = raw,
            receivedAt = DateTimeOffset.Now.ToString("HH:mm:ss")
        });
    }

    // A plain MVC action: no IDatastarService, no SSE. Datastar patches any
    // text/html response and reads the placement from the datastar-* headers.
    // Signals arrive in the query string on a GET; [FromSignals] binds a sub-path.
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromSignals(Path = "draft")] DraftSignals? draft)
    {
        var html = await renderer.RenderAsync("_DraftPreview", draft ?? new DraftSignals(), ControllerContext);

        Response.Headers["datastar-selector"] = "#preview";
        Response.Headers["datastar-mode"] = "inner";
        return Content(html, "text/html");
    }
}
