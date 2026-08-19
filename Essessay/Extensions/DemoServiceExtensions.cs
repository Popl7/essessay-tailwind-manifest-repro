using Essessay.Services;

namespace Essessay.Extensions;

public static class DemoServiceExtensions
{
    /// <summary>The process-wide singletons backing the Todos and Polling demo pages.</summary>
    public static WebApplicationBuilder AddDemoServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ITodoStore, TodoStore>();
        builder.Services.AddSingleton<IAttemptCounter, AttemptCounter>();

        return builder;
    }
}
