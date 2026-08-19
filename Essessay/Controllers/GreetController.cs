using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Models;

namespace Essessay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GreetController(IDatastarService datastar) : ControllerBase
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task Post()
    {
        // Read the client's signals, typed.
        var signals = await datastar.ReadSignalsAsync<GreetSignals>();

        var name = string.IsNullOrWhiteSpace(signals?.Name) ? "stranger" : signals!.Name!.Trim();
        var greeting = signals?.Style == "casual"
            ? $"Hey {name}! 👋"
            : $"Good day, {name}.";

        // Push a signal back to the client; $greeting updates reactively.
        // The SDK serializes the object to JSON, so pass the object (not a string).
        await datastar.PatchSignalsAsync(new { greeting });
    }
}
