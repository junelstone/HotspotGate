namespace StonesHotspot.Licensing;

/// <summary>Tells the licence check-in job that a code was just activated, so it contacts the server at once.</summary>
public sealed class LicenceSignals
{
    public event Action? Activated;

    public void RaiseActivated() => Activated?.Invoke();
}
