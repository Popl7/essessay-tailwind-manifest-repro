// .NET 8: no MapStaticAssets()/WithStaticAssets() (added in .NET 9). Static
// files are served by UseStaticFiles(), which reads straight from disk via
// WebRootFileProvider — it never consults a build-time manifest, unlike
// MapStaticAssets(). See README for why that likely changes whether this bug
// is observable as a runtime 404 at all on this TFM.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
