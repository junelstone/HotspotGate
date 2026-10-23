using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Dns;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Service;

/// <summary>
/// The heart of the service. Every second: charges the traffic counted by the gate to the sessions (time, data,
/// idle timeout) and rebuilds the table of devices allowed through. Every few seconds: makes the network match
/// the saved setup (gate first, then sharing) and reloads the web-filter rules. On stop: switches sharing off
/// (fail-closed).
/// </summary>
internal sealed class HotspotEngineService(
    IServiceScopeFactory scopes,
    IPacketGate gate,
    INeighborTable neighbors,
    PortalLocation portal,
    AccessRefreshSignal refresh,
    LiveTrafficMonitor speeds,
    ILicenseService licence,
    TimeProvider clock,
    ILogger<HotspotEngineService> logger) : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RulesInterval = TimeSpan.FromSeconds(10);

    /// <summary>Devices that moved data within this time count as "active" for fair share.</summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(10);

    private FilterRules _rules = FilterRules.Empty;
    private FairShare _fairShare = FairShare.Off;
    private string? _lastReconcileProblem;
    private DateTimeOffset _lastTick = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileAsync(stoppingToken);
        await ReloadRulesAsync(stoppingToken);

        var lastReconcile = clock.GetUtcNow();
        var lastRules = lastReconcile;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Every second, or at once when a login/logout asks for it.
                await refresh.WaitAsync(TickInterval, stoppingToken);
                if (refresh.TakeRulesReload())
                    await ReloadRulesAsync(stoppingToken);
                await TickAsync(stoppingToken);

                var now = clock.GetUtcNow();
                if (now - lastReconcile >= ReconcileInterval)
                {
                    lastReconcile = now;
                    await ReconcileAsync(stoppingToken);
                }
                if (now - lastRules >= RulesInterval)
                {
                    lastRules = now;
                    await ReloadRulesAsync(stoppingToken);
                    gate.Processor.Maintain();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotspot engine tick failed");
            }
        }
    }

    private int _stopped;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // The host may call this twice (stop, then dispose): sharing is switched off once, while services still exist.
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ShutdownAsync(cancellationToken);
            logger.LogInformation("Hotspot stopped: internet sharing switched off, packet gate closed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not switch internet sharing off while stopping");
        }
    }

    /// <summary>One engine step (also called directly by tests).</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var processor = gate.Processor;

        // 1. Charge the bytes counted since the last tick to the sessions they belong to.
        var current = processor.Snapshot;
        var traffic = new Dictionary<long, TrafficDelta>();
        foreach (var (ip, (up, down)) in processor.TakeByteCounts())
        {
            if (current?.Authorized.GetValueOrDefault(ip)?.SessionId is { } sessionId)
            {
                var previous = traffic.GetValueOrDefault(sessionId);
                traffic[sessionId] = new TrafficDelta(previous.BytesUp + up, previous.BytesDown + down);
            }
        }
        await services.GetRequiredService<SessionService>().TickAsync(traffic, ct);

        // 2. Rebuild the access table from the database (sessions just ended are gone from it).
        var config = gate.Status.Config;
        portal.Update(config?.Lan, config?.PortalPort ?? 80);
        if (config is null)
            return;

        var db = services.GetRequiredService<HotspotDbContext>();
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.EndedAt == null)
            .Select(s => new AccessSession(s.Id, s.Device.Mac, s.Ip, s.Tariff.DownloadKbps, s.Tariff.UploadKbps)).ToListAsync(ct);

        var now = clock.GetUtcNow();
        if (_lastTick != DateTimeOffset.MinValue)
            speeds.Record(traffic.ToDictionary(t => t.Key, t => (t.Value.BytesUp, t.Value.BytesDown)), sessions.Select(s => s.SessionId), now - _lastTick);
        _lastTick = now;
        var whitelisted = await db.Devices.AsNoTracking().Where(d => d.Whitelisted).Select(d => d.Mac).ToListAsync(ct);
        var blocked = await db.Devices.AsNoTracking().Where(d => d.Blocked).Select(d => d.Mac).ToListAsync(ct);

        IReadOnlyList<NeighborEntry> lanNeighbors;
        try
        {
            lanNeighbors = neighbors.GetIpv4Entries().Where(n => n.InterfaceIndex == config.LanInterfaceIndex).ToList();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "Could not read the neighbour table");
            lanNeighbors = [];
        }

        var authorized = AccessTableBuilder.Build(new AccessInputs(
            sessions, whitelisted, blocked, lanNeighbors, licence.GetStatus().HotspotAllowed));
        // Plan limits, lowered to an even share of the line when fair share is on.
        authorized = BandwidthPolicy.Apply(authorized, _fairShare, processor.RecentlyActive(ActiveWindow));
        processor.Update(new GateSnapshot(config.Lan, Ipv4Subnet.ToUInt32(config.Lan.Address), config.PortalPort, authorized, _rules));
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ReconcileAsync(ct);
            var problem = result.Succeeded ? null : string.Join("; ", result.Problems.Select(p => $"{p.Code}: {p.Detail}"));
            if (problem != _lastReconcileProblem)
            {
                if (problem is null)
                    logger.LogInformation("Network setup is active (gate {Gate})", gate.Status.State);
                else
                    logger.LogError("Network setup problem: {Problem}", problem);
                _lastReconcileProblem = problem;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Network reconciliation failed");
        }
    }

    /// <summary>Web-filter rules, family DNS and fair-share settings (reloaded every 10 s; also called by tests).</summary>
    internal async Task ReloadRulesAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var rules = await scope.ServiceProvider.GetRequiredService<WebFilterService>().ListEnabledAsync(ct);
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        _rules = ToFilterRules(rules) with { FamilyDns = await settings.GetBoolAsync(SettingKeys.FilterFamilyDns, ct) };
        _fairShare = new FairShare(
            await settings.GetBoolAsync(SettingKeys.FairShareEnabled, ct),
            await settings.GetIntAsync(SettingKeys.FairShareTotalDownKbps, 0, ct),
            await settings.GetIntAsync(SettingKeys.FairShareTotalUpKbps, 0, ct),
            await settings.GetIntAsync(SettingKeys.FairShareMinKbps, 256, ct),
            await settings.GetIntAsync(SettingKeys.FairShareMaxKbps, 0, ct));
    }

    internal static FilterRules ToFilterRules(IEnumerable<WebFilterRule> rules)
    {
        List<string> blockedDomains = [], freeDomains = [];
        List<Ipv4Subnet> blockedIps = [], freeIps = [];
        foreach (var rule in rules)
        {
            var isIp = IpRangeSet.TryParse(rule.Pattern, out var subnet);
            switch (rule.Kind, isIp)
            {
                case (WebFilterKind.Block, true): blockedIps.Add(subnet); break;
                case (WebFilterKind.Block, false): blockedDomains.Add(rule.Pattern); break;
                case (WebFilterKind.FreeAccess, true): freeIps.Add(subnet); break;
                case (WebFilterKind.FreeAccess, false): freeDomains.Add(rule.Pattern); break;
            }
        }
        return new FilterRules(new DomainSet(blockedDomains), new DomainSet(freeDomains), new IpRangeSet(blockedIps), new IpRangeSet(freeIps));
    }
}
