using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Ics;

namespace StonesHotspot.Network.Setup;

public sealed record NetworkConfiguration(NetworkMode Mode, string? WanAdapterId, string? LanAdapterId);

public sealed record NetworkOverview(
    bool IsAdministrator,
    bool IsWindows,
    NetworkConfiguration Configuration,
    IReadOnlyList<NetworkAdapterInfo> Adapters,
    AdapterSuggestion Suggestion,
    IcsStatus Ics,
    GateStatus Gate,
    IReadOnlyList<NetworkProblem> Problems)
{
    public NetworkAdapterInfo? Wan => Adapters.FirstOrDefault(a => a.Id == Configuration.WanAdapterId);
    public NetworkAdapterInfo? Lan => Adapters.FirstOrDefault(a => a.Id == Configuration.LanAdapterId);
}

public sealed record NetworkApplyResult(bool Succeeded, IReadOnlyList<NetworkProblem> Problems);

/// <summary>Settings of the network layer that come from the service configuration.</summary>
public sealed class HotspotNetworkOptions
{
    /// <summary>TCP port of the captive portal on the gateway (80 in production).</summary>
    public ushort PortalPort { get; set; } = 80;
}

/// <summary>
/// Remembers the WAN/LAN choice and makes the PC match it: packet gate first, then internet sharing (so the
/// customer network is never shared without filtering).
/// </summary>
public sealed class NetworkSetupService(
    INetworkAdapterProvider adapterProvider,
    IIcsController ics,
    IPacketGate gate,
    IPrivilegeChecker privileges,
    HotspotNetworkOptions options,
    SettingsService settings,
    AuditService audit)
{
    /// <summary>Windows ICS always gives the shared adapter this address unless changed in the registry.</summary>
    public static readonly Ipv4Subnet DefaultIcsLan = Ipv4Subnet.Parse("192.168.137.1/24");

    public async Task<NetworkConfiguration> GetConfigurationAsync(CancellationToken ct = default) =>
        new(Enum.TryParse<NetworkMode>(await settings.GetAsync(SettingKeys.NetworkMode, ct), out var mode) ? mode : NetworkMode.None,
            NullIfEmpty(await settings.GetAsync(SettingKeys.NetworkWanAdapterId, ct)),
            NullIfEmpty(await settings.GetAsync(SettingKeys.NetworkLanAdapterId, ct)));

    public async Task<NetworkOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var config = await GetConfigurationAsync(ct);
        var adapters = adapterProvider.GetAdapters();
        var status = ReadIcsStatus(out var icsError);

        var problems = new List<NetworkProblem>();
        if (config.Mode != NetworkMode.None)
        {
            problems.AddRange(NetworkSetupValidator.Validate(adapters, config.WanAdapterId, config.LanAdapterId, privileges.IsAdministrator, privileges.IsWindows));
            if (config.Mode == NetworkMode.Ics && status.Supported && !status.IsSharing(config.WanAdapterId!, config.LanAdapterId!))
                problems.Add(NetworkProblem.Warning(NetworkProblemCode.IcsNotActive));
            if (gate.Status.State == GateState.Failed)
                problems.Add(NetworkProblem.Error(NetworkProblemCode.GateFailed, gate.Status.Error));
            else if (!gate.Status.IsRunning && privileges.IsWindows)
                problems.Add(NetworkProblem.Warning(NetworkProblemCode.GateNotRunning));
        }
        else
        {
            if (!privileges.IsWindows)
                problems.Add(NetworkProblem.Error(NetworkProblemCode.NotWindows));
            if (!privileges.IsAdministrator)
                problems.Add(NetworkProblem.Error(NetworkProblemCode.NotAdministrator));
        }
        if (icsError is not null)
            problems.Add(NetworkProblem.Error(NetworkProblemCode.IcsFailed, icsError));

        return new NetworkOverview(privileges.IsAdministrator, privileges.IsWindows, config, adapters,
            AdapterAdvisor.Suggest(adapters), status, gate.Status, problems);
    }

    /// <summary>Validates the choice, starts the gate, shares the internet from WAN to LAN and saves the choice.</summary>
    public async Task<NetworkApplyResult> ApplyAsync(string? wanId, string? lanId, Staff? actor, CancellationToken ct = default)
    {
        var adapters = adapterProvider.GetAdapters();
        var problems = NetworkSetupValidator.Validate(adapters, wanId, lanId, privileges.IsAdministrator, privileges.IsWindows).ToList();
        if (problems.HasErrors())
            return new NetworkApplyResult(false, problems);

        var wan = adapters.First(a => SameId(a.Id, wanId!));
        var lan = adapters.First(a => SameId(a.Id, lanId!));
        var config = new NetworkConfiguration(lan.IsMobileHotspot ? NetworkMode.MobileHotspot : NetworkMode.Ics, wan.Id, lan.Id);

        // Saved first: the engine reconciles from the saved choice and must not undo this one.
        await SaveAsync(config, actor, ct);
        var result = Reconcile(config, adapters);
        if (!result.Succeeded)
        {
            await SaveAsync(config with { Mode = NetworkMode.None }, actor, ct);
            return result with { Problems = [.. problems, .. result.Problems] };
        }

        audit.Record(actor, "Network.SharingEnabled", $"{config.Mode}: {wan.Name} → {lan.Name}");
        await settings.SetAsync(SettingKeys.NetworkMode, config.Mode.ToString(), actor, ct); // saves the audit entry
        return new NetworkApplyResult(true, problems);
    }

    /// <summary>Stops internet sharing and the gate. The adapter choice is kept so it can be switched on again.</summary>
    public async Task<NetworkApplyResult> DisableAsync(Staff? actor, CancellationToken ct = default)
    {
        var config = await GetConfigurationAsync(ct);
        if (config.Mode == NetworkMode.Ics)
        {
            try
            {
                ics.Disable();
            }
            catch (IcsException ex)
            {
                return new NetworkApplyResult(false, [NetworkProblem.Error(NetworkProblemCode.IcsFailed, ex.Message)]);
            }
        }
        gate.Stop();

        audit.Record(actor, "Network.SharingDisabled");
        await SaveAsync(config with { Mode = NetworkMode.None }, actor, ct);
        return new NetworkApplyResult(true, []);
    }

    /// <summary>
    /// Makes the PC match the saved choice. Called at start and every few seconds by the engine: restarts the gate
    /// when the LAN address changes, switches ICS back on when Windows dropped it, and switches ICS off when the
    /// gate cannot run.
    /// </summary>
    public async Task<NetworkApplyResult> ReconcileAsync(CancellationToken ct = default) =>
        Reconcile(await GetConfigurationAsync(ct), adapterProvider.GetAdapters());

    /// <summary>Fail-closed stop (service shutting down): no internet sharing without the gate.</summary>
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        var config = await GetConfigurationAsync(ct);
        if (config.Mode == NetworkMode.Ics)
        {
            try
            {
                ics.Disable();
            }
            catch (IcsException)
            {
                // Nothing more we can do while stopping; the start-up reconcile repairs the state.
            }
        }
        gate.Stop();
    }

    /// <summary>The gate settings for a configuration, or null when the LAN adapter has no usable address yet.</summary>
    public GateConfig? GateConfigFor(NetworkConfiguration config, IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        var lan = adapters.FirstOrDefault(a => SameId(a.Id, config.LanAdapterId ?? ""));
        if (lan is null || config.Mode == NetworkMode.None)
            return null;

        var address = lan.Addresses.Where(a => !IsLinkLocal(a)).Cast<Ipv4Subnet?>().FirstOrDefault();
        if (address is null && config.Mode == NetworkMode.Ics)
            address = DefaultIcsLan; // ICS will give the adapter this address once sharing is on
        return address is null || lan.InterfaceIndex < 0 ? null : new GateConfig(lan.InterfaceIndex, address.Value, options.PortalPort);
    }

    private NetworkApplyResult Reconcile(NetworkConfiguration config, IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        if (config.Mode == NetworkMode.None || config.WanAdapterId is null || config.LanAdapterId is null)
        {
            gate.Stop();
            return new NetworkApplyResult(true, []);
        }

        var gateConfig = GateConfigFor(config, adapters);
        if (gateConfig is null)
        {
            gate.Stop();
            var code = config.Mode == NetworkMode.MobileHotspot ? NetworkProblemCode.MobileHotspotOff : NetworkProblemCode.AdapterNotFound;
            return new NetworkApplyResult(false, [NetworkProblem.Error(code, config.LanAdapterId)]);
        }

        if (!gate.Start(gateConfig))
        {
            var problem = NetworkProblem.Error(NetworkProblemCode.GateFailed, gate.Status.Error);
            if (config.Mode == NetworkMode.Ics)
            {
                try
                {
                    ics.Disable(); // never share the customer network without the filter
                }
                catch (IcsException ex)
                {
                    return new NetworkApplyResult(false, [problem, NetworkProblem.Error(NetworkProblemCode.IcsFailed, ex.Message)]);
                }
            }
            return new NetworkApplyResult(false, [problem]);
        }

        if (config.Mode == NetworkMode.Ics)
        {
            try
            {
                if (!ics.GetStatus().IsSharing(config.WanAdapterId, config.LanAdapterId))
                    ics.Enable(config.WanAdapterId, config.LanAdapterId);
            }
            catch (Exception ex) when (ex is IcsException or PlatformNotSupportedException)
            {
                return new NetworkApplyResult(false, [NetworkProblem.Error(NetworkProblemCode.IcsFailed, ex.Message)]);
            }
        }
        return new NetworkApplyResult(true, []);
    }

    private async Task SaveAsync(NetworkConfiguration config, Staff? actor, CancellationToken ct)
    {
        await settings.SetAsync(SettingKeys.NetworkWanAdapterId, config.WanAdapterId ?? "", actor, ct);
        await settings.SetAsync(SettingKeys.NetworkLanAdapterId, config.LanAdapterId ?? "", actor, ct);
        await settings.SetAsync(SettingKeys.NetworkMode, config.Mode.ToString(), actor, ct);
    }

    private IcsStatus ReadIcsStatus(out string? error)
    {
        error = null;
        try
        {
            return ics.GetStatus();
        }
        catch (IcsException ex)
        {
            error = ex.Message;
            return IcsStatus.NotSupported;
        }
    }

    private static bool IsLinkLocal(Ipv4Subnet subnet) => subnet.Address.GetAddressBytes() is [169, 254, ..];

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
