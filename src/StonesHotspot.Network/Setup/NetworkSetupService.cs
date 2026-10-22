using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Adapters;
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
    IReadOnlyList<NetworkProblem> Problems)
{
    public NetworkAdapterInfo? Wan => Adapters.FirstOrDefault(a => a.Id == Configuration.WanAdapterId);
    public NetworkAdapterInfo? Lan => Adapters.FirstOrDefault(a => a.Id == Configuration.LanAdapterId);
}

public sealed record NetworkApplyResult(bool Succeeded, IReadOnlyList<NetworkProblem> Problems);

/// <summary>Remembers the WAN/LAN choice and turns internet sharing on or off.</summary>
public sealed class NetworkSetupService(
    INetworkAdapterProvider adapterProvider,
    IIcsController ics,
    IPrivilegeChecker privileges,
    SettingsService settings,
    AuditService audit)
{
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
            AdapterAdvisor.Suggest(adapters), status, problems);
    }

    /// <summary>Validates the choice, shares the internet from WAN to LAN and saves the choice.</summary>
    public async Task<NetworkApplyResult> ApplyAsync(string? wanId, string? lanId, Staff? actor, CancellationToken ct = default)
    {
        var adapters = adapterProvider.GetAdapters();
        var problems = NetworkSetupValidator.Validate(adapters, wanId, lanId, privileges.IsAdministrator, privileges.IsWindows).ToList();
        if (problems.HasErrors())
            return new NetworkApplyResult(false, problems);

        var lan = adapters.First(a => string.Equals(a.Id, lanId, StringComparison.OrdinalIgnoreCase));
        var mode = lan.IsMobileHotspot ? NetworkMode.MobileHotspot : NetworkMode.Ics;
        if (mode == NetworkMode.Ics)
        {
            try
            {
                ics.Enable(wanId!, lanId!);
            }
            catch (Exception ex) when (ex is IcsException or PlatformNotSupportedException)
            {
                problems.Add(NetworkProblem.Error(NetworkProblemCode.IcsFailed, ex.Message));
                return new NetworkApplyResult(false, problems);
            }
        }

        var wan = adapters.First(a => string.Equals(a.Id, wanId, StringComparison.OrdinalIgnoreCase));
        audit.Record(actor, "Network.SharingEnabled", $"{mode}: {wan.Name} → {lan.Name}");
        await SaveAsync(new NetworkConfiguration(mode, wan.Id, lan.Id), actor, ct);
        return new NetworkApplyResult(true, problems);
    }

    /// <summary>Stops internet sharing. The adapter choice is kept so it can be switched on again.</summary>
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

        audit.Record(actor, "Network.SharingDisabled");
        await SaveAsync(config with { Mode = NetworkMode.None }, actor, ct);
        return new NetworkApplyResult(true, []);
    }

    /// <summary>
    /// Called at service start: Windows sometimes drops ICS after a reboot or an update, so switch it back on
    /// if it was configured. Returns null when nothing needed to be done.
    /// </summary>
    public async Task<NetworkApplyResult?> EnsureAppliedAsync(CancellationToken ct = default)
    {
        var config = await GetConfigurationAsync(ct);
        if (config.Mode != NetworkMode.Ics || config.WanAdapterId is null || config.LanAdapterId is null)
            return null;

        var status = ReadIcsStatus(out _);
        if (status.IsSharing(config.WanAdapterId, config.LanAdapterId))
            return null;

        return await ApplyAsync(config.WanAdapterId, config.LanAdapterId, null, ct);
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

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
