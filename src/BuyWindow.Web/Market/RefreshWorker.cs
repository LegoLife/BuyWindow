namespace BuyWindow.Web.Market;

/// <summary>Checks regularly whether a scheduled refresh is due, which also covers restarts and failed attempts.</summary>
public sealed class RefreshWorker(MarketStore store, AlertSender alerts, ILogger<RefreshWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckEvery);
        do
        {
            if (!store.IsConfigured) continue;
            try
            {
                await store.RefreshDueAsync(ct: stoppingToken);
                if (store.LastError is { } error)
                    logger.LogWarning("Price refresh failed: {Error}", error);

                await alerts.SendDueAsync(stoppingToken);
                store.MarkChecked();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Price refresh crashed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
