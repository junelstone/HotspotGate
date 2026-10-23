using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Online;
using StonesHotspot.Web;

namespace StonesHotspot.Service;

/// <summary>
/// Asks the licence server once a day whether a newer version is published. Only a manifest signed with the server
/// key is believed; admins then see it on the dashboard and the tray app offers to install it.
/// </summary>
public sealed class UpdateCheckService(
    UpdateStatus status, LicensingOptions keys, IConfiguration configuration, TimeProvider clock, ILogger<UpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = configuration["Licensing:ServerUrl"];
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var server))
            return;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new LicenceServerClient(http, server, HotspotEndpoints.Version);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24), clock);
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), clock, stoppingToken);
            do
            {
                try
                {
                    await RunOnceAsync(client, stoppingToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation("Update check failed ({Error}); next try tomorrow", ex.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task RunOnceAsync(LicenceServerClient client, CancellationToken ct)
    {
        var signed = await client.GetUpdateAsync(ct);
        var manifest = signed?.Verify(keys.ServerPublicKey);
        if (signed is not null && manifest is null)
            logger.LogWarning("The update information from the licence server is not signed with the server key: ignored");
        status.Set(manifest, HotspotEndpoints.Version, clock.GetUtcNow());
        if (status.Available is { } update)
            logger.LogInformation("Version {Version} is available (running {Current})", update.Version, HotspotEndpoints.Version);
    }
}
