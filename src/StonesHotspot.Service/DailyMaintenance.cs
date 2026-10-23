using StonesHotspot.Core.Data;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Service;

/// <summary>
/// Once a day (the first time 10 minutes after start): automatic database backup (the 14 latest kept), then removal
/// of personal data older than the retention setting from the connection logs.
/// </summary>
public sealed class DailyMaintenance(IServiceScopeFactory scopes, ILogger<DailyMaintenance> logger) : BackgroundService
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<BackupService>().CreateAutomaticAsync(ct) is { } name)
                logger.LogInformation("Automatic backup {Name}", name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogError(ex, "Automatic backup failed");
        }

        // After the backup, so the backup of the day still has what the retention removes today.
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<RetentionService>().ApplyAsync(ct);
            if (result.Total > 0)
                logger.LogInformation("Connection logs past retention: {Sessions} session IPs removed, {Devices} devices anonymised, {Masked} phone numbers masked",
                    result.Sessions, result.Devices, result.Phones);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Retention of connection logs failed");
        }
    }
}
