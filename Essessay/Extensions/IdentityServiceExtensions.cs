using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Essessay.Data;
using Essessay.Services;

namespace Essessay.Extensions;

public static class IdentityServiceExtensions
{
    /// <summary>The database, ASP.NET Core Identity, and the email sender it needs to work at all.</summary>
    public static WebApplicationBuilder AddEssessayIdentity(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
                                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
            .AddEntityFrameworkStores<ApplicationDbContext>();

        // Registering an IEmailSender is what makes confirmation work at all: without one
        // Identity falls back to a no-op that drops the message, so the link a new account
        // needs is generated and thrown away. This one logs it instead of sending it.
        builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();

        return builder;
    }
}
