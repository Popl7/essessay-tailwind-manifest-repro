using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Models;
using Essessay.Services;

namespace Essessay.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Fragment()
    {
        return View();
    }

    public IActionResult LiveStream()
    {
        return View();
    }

    public IActionResult Signals()
    {
        return View();
    }

    public IActionResult RoundTrip()
    {
        return View();
    }

    public IActionResult Feed()
    {
        return View();
    }

    public IActionResult Todos([FromServices] ITodoStore store)
    {
        // Newest first to match the prepend-on-add behaviour.
        return View(store.All().Reverse().ToList());
    }

    public async Task<IActionResult> Board([FromServices] IBoardStore store)
    {
        // The initial render is the same partial the stream sends on connect.
        return View(await store.AllAsync());
    }

    public IActionResult Errors()
    {
        return View();
    }

    public IActionResult Forms()
    {
        return View(new ContactForm());
    }

    public IActionResult Polling()
    {
        return View();
    }

    // Posted by a real <form>, either by Datastar (contentType: 'form') or by the
    // browser itself when JavaScript is unavailable. The antiforgery token rides
    // along in the form's hidden field in both cases — no X-CSRF-TOKEN header needed.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contact(
        ContactForm form,
        [FromServices] IDatastarService datastar,
        [FromServices] IPartialRenderer renderer)
    {
        // Datastar sets this header on every request it makes; without it we're a
        // plain browser post and have to answer with a full page instead of a patch.
        var isDatastarRequest = Request.Headers.ContainsKey("Datastar-Request");

        if (!ModelState.IsValid)
        {
            if (!isDatastarRequest) return View(nameof(Forms), form);

            // Re-render the form with ModelState so the validation messages come
            // back inside the patch (default outer mode morphs #contact-form).
            var invalidHtml = await renderer.RenderAsync("_ContactForm", form, ControllerContext, ModelState);
            await datastar.PatchElementsAsync(invalidHtml);
            return new EmptyResult();
        }

        if (!isDatastarRequest)
        {
            TempData["ContactSent"] = $"Thanks {form.Name}, your message was sent (full page post).";
            return RedirectToAction(nameof(Forms));
        }

        // Inner-patch the result panel, then swap in a fresh, empty form.
        var resultHtml = await renderer.RenderAsync("_ContactResult", form, ControllerContext);
        await datastar.PatchElementsAsync(resultHtml, new PatchElementsOptions
        {
            Selector = "#contact-result",
            PatchMode = ElementPatchMode.Inner,
            UseViewTransition = true
        });

        var emptyFormHtml = await renderer.RenderAsync("_ContactForm", new ContactForm(), ControllerContext);
        await datastar.PatchElementsAsync(emptyFormHtml);
        return new EmptyResult();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
