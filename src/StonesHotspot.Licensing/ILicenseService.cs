namespace StonesHotspot.Licensing;

public enum ActivationResult
{
    Activated,

    /// <summary>Not a licence code, mistyped, edited or not signed by Stone's IT Consulting.</summary>
    Invalid,

    /// <summary>A real code, but for another PC.</summary>
    OtherMachine,

    Expired,

    /// <summary>An older code of the licence already active.</summary>
    Older,
}

/// <summary>The only place the app asks "may I do this?". Used by the service, web and tray.</summary>
public interface ILicenseService
{
    LicenseStatus GetStatus();

    /// <summary>True when the current edition includes <paramref name="feature"/> and the hotspot may run.</summary>
    bool IsAllowed(Feature feature);

    /// <summary>This PC's Machine ID, e.g. <c>MID-7K3Q-9XPA-2LMD</c>, to send to the owner.</summary>
    string MachineId { get; }

    /// <summary>Checks a licence code (or <c>.lic</c> file content) and, when valid for this PC, uses it at once.</summary>
    ActivationResult Activate(string code);
}
