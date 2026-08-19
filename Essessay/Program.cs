using Microsoft.EntityFrameworkCore;
using Essessay.Data;
using Essessay.Extensions;
using Essessay.Services;

var builder = WebApplication.CreateBuilder(args);

// Machine-specific overrides that shouldn't be committed — a personal Redis
// instance, say — as distinct from appsettings.Development.json, which every
// clone gets. Gitignored; optional so its absence is silent everywhere else.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.AddEssessayIdentity();
builder.AddEssessayMvc();
builder.AddDemoServices();

// The board runs on one instance out of the box. Point ConnectionStrings:Redis at
// a server and the same board spans every instance sharing it — same endpoints,
// same views, only the backplane and the card store change.
var redis = builder.AddRedisIfConfigured();
builder.AddEssessayDataProtection(redis);
builder.AddBoard();

builder.AddEssessayRateLimiting();
builder.AddProxyForwarding();

var app = builder.Build();

// The image ships no app.db and the file is gitignored, so without this a fresh
// deployment answers its first login with "no such table: AspNetUsers". In
// development the migrations endpoint offers a button for it; nowhere else does.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
}

app.UseProxyForwardingIfConfigured();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
    .WithStaticAssets()
    .RequireRateLimiting(RateLimits.Identity);

app.MapEssessayHealthChecks();

app.Run();
