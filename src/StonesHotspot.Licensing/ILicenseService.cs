namespace StonesHotspot.Licensing;

/// <summary>The only place the app asks "may I do this?". Used by the service, web and tray.</summary>
public interface ILicenseService
{
    LicenseStatus GetStatus();

    /// <summary>True when the current edition includes <paramref name="feature"/> and the hotspot may run.</summary>
    bool IsAllowed(Feature feature);
}
