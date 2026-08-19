using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Essessay.Services;

public interface IPartialRenderer
{
    /// <summary>
    /// Renders a partial view to an HTML string (e.g. to send as a Datastar SSE fragment).
    /// Pass <paramref name="modelState"/> to carry validation errors into the fragment
    /// (asp-validation-for / asp-validation-summary read them from there).
    /// </summary>
    Task<string> RenderAsync(string viewName, object? model, ActionContext actionContext,
        ModelStateDictionary? modelState = null);
}

public class PartialRenderer(ICompositeViewEngine viewEngine, ITempDataProvider tempDataProvider) : IPartialRenderer
{
    public async Task<string> RenderAsync(string viewName, object? model, ActionContext actionContext,
        ModelStateDictionary? modelState = null)
    {
        var viewResult = viewEngine.FindView(actionContext, viewName, isMainPage: false);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException(
                $"Partial '{viewName}' not found. Searched: {string.Join(", ", viewResult.SearchedLocations ?? [])}");
        }

        using var writer = new StringWriter();
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState ?? new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(actionContext.HttpContext, tempDataProvider);
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());
        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
