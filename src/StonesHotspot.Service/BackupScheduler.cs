using StonesHotspot.Core.Data;

namespace StonesHotspot.Service;

/// <summary>Daily automatic database backup (the first one 10 minutes after start), keeping the 14 latest.</summary>
public sealed class BackupScheduler(IServiceScopeFactory scopes, ILogger<BackupScheduler> logger) : BackgroundService
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
    }
}
