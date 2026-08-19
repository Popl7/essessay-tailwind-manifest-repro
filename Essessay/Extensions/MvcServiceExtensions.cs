using Essessay.Services;
using StarFederation.Datastar.DependencyInjection;
using StarFederation.Datastar.ModelBinding;

namespace Essessay.Extensions;

public static class MvcServiceExtensions
{
    /// <summary>MVC, Datastar, and the antiforgery setup both of them rely on.</summary>
    public static WebApplicationBuilder AddEssessayMvc(this WebApplicationBuilder builder)
    {
        var mvcBuilder = builder.Services.AddControllersWithViews();

        if (builder.Environment.IsDevelopment())
        {
            mvcBuilder.AddRazorRuntimeCompilation();
        }

        // AddDatastarMvc() registers the model binder behind [FromSignals], so signals can
        // bind straight to action parameters instead of calling ReadSignalsAsync<T>().
        builder.Services.AddDatastar().AddDatastarMvc();
        builder.Services.AddScoped<IPartialRenderer, PartialRenderer>();

        // Antiforgery token delivered via a header so Datastar fetch requests can send it.
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

        return builder;
    }
}
