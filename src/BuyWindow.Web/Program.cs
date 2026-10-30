using BuyWindow.Web.Components;
using BuyWindow.Web.Data;
using BuyWindow.Web.Market;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=buywindow.db";
builder.Services.AddDbContextFactory<AppDb>(options => options.UseSqlite(connectionString));

if (builder.Configuration["DataProtection:KeysPath"] is { } keysPath)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IMarketData, TiingoClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Tiingo:BaseUrl"] ?? "https://api.tiingo.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<MarketStore>();
builder.Services.AddHostedService<RefreshWorker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
