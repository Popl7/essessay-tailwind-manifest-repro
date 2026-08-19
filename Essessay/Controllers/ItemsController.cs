using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Services;

namespace Essessay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ItemsController(IDatastarService datastar, IPartialRenderer renderer) : ControllerBase
{
    private const int PageSize = 5;
    private const int TotalPages = 5;

    [HttpGet]
    public async Task Get(int page = 1)
    {
        var items = Enumerable.Range((page - 1) * PageSize + 1, PageSize)
            .Select(i => $"Item {i}")
            .ToList();

        // Append the new page of items into the feed list.
        var itemsHtml = await renderer.RenderAsync("_FeedItems", items, ControllerContext);
        await datastar.PatchElementsAsync(itemsHtml, new PatchElementsOptions
        {
            Selector = "#feed",
            PatchMode = ElementPatchMode.Append
        });

        // Replace the sentinel with the next one (or an end marker) — default outer mode.
        int? nextPage = page < TotalPages ? page + 1 : null;
        var sentinelHtml = await renderer.RenderAsync("_Sentinel", nextPage, ControllerContext);
        await datastar.PatchElementsAsync(sentinelHtml);
    }
}
