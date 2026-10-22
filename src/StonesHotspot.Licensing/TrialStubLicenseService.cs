namespace StonesHotspot.Licensing;

/// <summary>
/// Placeholder until the real licensing phase: always reports a Pro trial that ends 30 days after the
/// process started. Replaced by the signed-licence implementation without touching feature code.
/// </summary>
public sealed class TrialStubLicenseService(TimeProvider clock) : ILicenseService
{
    public const int TrialDays = 30;

    private readonly DateTimeOffset _trialEnds = clock.GetUtcNow().AddDays(TrialDays);

    public LicenseStatus GetStatus() =>
        new(Edition.Trial, _trialEnds, InGracePeriod: false, HotspotAllowed: true);

    public bool IsAllowed(Feature feature)
    {
        var status = GetStatus();
        return status.HotspotAllowed && EditionFeatures.Includes(status.Edition, feature);
    }
}
