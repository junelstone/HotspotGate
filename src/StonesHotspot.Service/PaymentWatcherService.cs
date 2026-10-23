using StonesHotspot.Core.Data;
using StonesHotspot.Core.Payments;
using StonesHotspot.Licensing;
using Microsoft.EntityFrameworkCore;

namespace StonesHotspot.Service;

/// <summary>
/// Every few seconds, asks the operators about pending Mobile Money payments, so a payment completes (code, sale,
/// SMS) even when the customer closed the waiting page and no callback arrives.
/// </summary>
public sealed class PaymentWatcherService(IServiceScopeFactory scopes, ILicenseService licence, ILogger<PaymentWatcherService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Mobile Money check failed");
            }
        }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        if (!licence.IsAllowed(Feature.MobileMoneyPayments))
            return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HotspotDbContext>();
        if (!await db.MobilePayments.AnyAsync(p => p.Status == Core.Data.Entities.MobilePaymentStatus.Pending, ct))
            return;
        await scope.ServiceProvider.GetRequiredService<MobileMoneyService>().CheckPendingAsync(ct);
    }
}
