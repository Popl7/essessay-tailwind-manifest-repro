using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Services;

namespace Essessay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EndpointController(IDatastarService datastar, IPartialRenderer renderer) : ControllerBase
{
    [HttpGet]
    public async Task Get()
    {
        var html = await renderer.RenderAsync("_EndpointCard", DateTimeOffset.UtcNow, ControllerContext);
        await datastar.PatchElementsAsync(html, new PatchElementsOptions { UseViewTransition = true });
    }
}
