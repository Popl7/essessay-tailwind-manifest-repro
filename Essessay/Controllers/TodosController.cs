using Microsoft.AspNetCore.Mvc;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;
using Essessay.Models;
using Essessay.Services;

namespace Essessay.Controllers;

[ApiController]
[Route("api/todos")]
[ValidateAntiForgeryToken] // requires the X-CSRF-TOKEN header (see Program.cs)
public class TodosController(IDatastarService datastar, IPartialRenderer renderer, ITodoStore store) : ControllerBase
{
    [HttpPost]
    public async Task Add()
    {
        var signals = await datastar.ReadSignalsAsync<TodoSignals>();
        var text = signals?.NewTodo?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            await datastar.ExecuteScriptAsync("window.showToast('Please enter a todo', 'error')");
            return;
        }

        var todo = store.Add(text);
        var html = await renderer.RenderAsync("_TodoItem", todo, ControllerContext);

        // Newest first: prepend the new item to the list.
        await datastar.PatchElementsAsync(html, new PatchElementsOptions
        {
            Selector = "#todo-list",
            PatchMode = ElementPatchMode.Prepend
        });
        await datastar.PatchSignalsAsync(new { newTodo = "" });
        await datastar.ExecuteScriptAsync("window.showToast('Added')");
    }

    [HttpPatch("{id:int}/toggle")]
    public async Task Toggle(int id)
    {
        var todo = store.Toggle(id);
        if (todo is null) return;

        // Outer replace of #todo-{id}, animated.
        var html = await renderer.RenderAsync("_TodoItem", todo, ControllerContext);
        await datastar.PatchElementsAsync(html, new PatchElementsOptions { UseViewTransition = true });
    }

    [HttpDelete("{id:int}")]
    public async Task Delete(int id)
    {
        if (!store.Remove(id)) return;

        // Server-initiated element removal.
        await datastar.RemoveElementAsync($"#todo-{id}");
        await datastar.ExecuteScriptAsync("window.showToast('Deleted')");
    }
}
