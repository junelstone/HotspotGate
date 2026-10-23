using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Online;
using StonesHotspot.Web;

namespace StonesHotspot.Service;

/// <summary>
/// Talks to the licence server: registers the trial once, then checks the licence in every day (renewals and
/// revocations). Without a server address, or when it cannot be reached, the hotspot keeps working offline until the
/// licence ends; it tries again an hour later.
/// </summary>
public sealed class LicenceCheckInService : BackgroundService
{
    private readonly LicenseService licence;
    private readonly IConfiguration configuration;
    private readonly ILogger<LicenceCheckInService> logger;

    public LicenceCheckInService(LicenseService licence, LicenceSignals signals, IConfiguration configuration, ILogger<LicenceCheckInService> logger)
    {
        this.licence = licence;
        this.configuration = configuration;
        this.logger = logger;
        signals.Activated += TriggerNow;
    }

    public static readonly TimeSpan Daily = TimeSpan.FromHours(24);
    public static readonly TimeSpan Retry = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _wake = new(0);

    /// <summary>Check in now (after an activation).</summary>
    public void TriggerNow() => _wake.Release();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = configuration["Licensing:ServerUrl"];
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var server))
        {
            logger.LogInformation("No licence server configured: licences are checked offline only");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new LicenceServerClient(http, server, HotspotEndpoints.Version);
        await Wait(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = Daily;
            try
            {
                await RunOnceAsync(client, stoppingToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Licence server not reachable ({Error}); retry in 1 hour", ex.Message);
                next = Retry;
            }
            await Wait(next, stoppingToken);
        }
    }

    public async Task RunOnceAsync(LicenceServerClient client, CancellationToken ct)
    {
        if (licence.Licence is { } current)
        {
            var (answer, nonce) = await client.CheckInAsync(current.Id, licence.MachineId, current.ExpiresOn, ct);
            licence.ApplyServerAnswer(answer, nonce);
        }
        else if (!licence.TrialRegistered)
        {
            var (answer, nonce) = await client.RegisterTrialAsync(licence.MachineId, licence.TrialStartedAt, ct);
            licence.ApplyServerAnswer(answer, nonce);
        }
    }

    private async Task Wait(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await _wake.WaitAsync(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
