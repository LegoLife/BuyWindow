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
builder.Services.AddHttpClient(SecClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<SecClient>(); // a singleton, so its cache lasts
builder.Services.AddSingleton<FinvizClient>(); // a singleton, so its cache lasts
builder.Services.AddSingleton<MarketStore>();
builder.Services.AddSingleton<OwnService>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<AlertSender>();
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

if (app.Environment.IsDevelopment())
{
    // For checking how the buy score and the Lynch checks line up across the watchlist.
    app.MapGet("/dev/lynch-vs-score", async (MarketStore store, SecClient sec) =>
    {
        var rows = new List<object>();
        foreach (var ticker in await store.LoadWatchlistAsync())
        {
            if (await sec.FinancialsAsync(ticker.Symbol) is not { } financials) { rows.Add(new { ticker.Symbol, NoFilings = true }); continue; }
            var r = BuyWindow.Core.LynchVsScore.Run(ticker.Analysis, financials);
            rows.Add(new
            {
                ticker.Symbol, r.SignalShare, r.DeadlineShare, r.Correlation, r.Days,
                SignalMonths = r.Buys.Count(b => b.Reason == BuyWindow.Core.BuyReason.Signal),
                Months = r.Buys.Count,
                Signals = r.Buys.Where(b => b.Reason == BuyWindow.Core.BuyReason.Signal).Select(b => $"{b.BuyDate} {b.Passed}/{b.Checked}"),
            });
        }
        return rows;
    });
}

app.Run();
