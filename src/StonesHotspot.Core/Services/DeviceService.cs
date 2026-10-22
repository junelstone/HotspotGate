using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;

namespace StonesHotspot.Core.Services;

public sealed class DeviceService(HotspotDbContext db, AuditService audit, TimeProvider clock)
{
    public Task<Device?> FindByMacAsync(string mac, CancellationToken ct = default)
    {
        var normalized = MacAddress.Normalize(mac);
        return db.Devices.FirstOrDefaultAsync(d => d.Mac == normalized, ct);
    }

    /// <summary>Finds or creates the device and updates its IP, hostname and last-seen time (not saved).</summary>
    public async Task<Device> TouchAsync(string mac, string? ip, string? hostname = null, CancellationToken ct = default)
    {
        var normalized = MacAddress.Normalize(mac);
        var now = clock.GetUtcNow().UtcDateTime;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Mac == normalized, ct);
        if (device is null)
        {
            device = new Device { Mac = normalized, FirstSeen = now };
            db.Devices.Add(device);
        }

        device.LastSeen = now;
        if (!string.IsNullOrWhiteSpace(ip))
            device.LastIp = ip;
        if (!string.IsNullOrWhiteSpace(hostname))
            device.Hostname = hostname;
        return device;
    }

    public async Task SetWhitelistedAsync(string mac, bool whitelisted, Staff? actor, CancellationToken ct = default)
    {
        var device = await TouchAsync(mac, null, null, ct);
        device.Whitelisted = whitelisted;
        if (whitelisted)
            device.Blocked = false;
        audit.Record(actor, whitelisted ? "Device.Whitelisted" : "Device.Unwhitelisted", device.Mac);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Blocks a device. Its sessions are ended by the next <see cref="SessionService.TickAsync"/>.</summary>
    public async Task SetBlockedAsync(string mac, bool blocked, Staff? actor, CancellationToken ct = default)
    {
        var device = await TouchAsync(mac, null, null, ct);
        device.Blocked = blocked;
        if (blocked)
            device.Whitelisted = false;
        audit.Record(actor, blocked ? "Device.Blocked" : "Device.Unblocked", device.Mac);
        await db.SaveChangesAsync(ct);
    }
}
