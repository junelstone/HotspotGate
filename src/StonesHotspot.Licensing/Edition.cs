namespace StonesHotspot.Licensing;

public enum Edition
{
    /// <summary>No valid trial or licence (after the grace period): hotspot stopped, data still accessible.</summary>
    None,

    /// <summary>30-day trial with every Pro feature.</summary>
    Trial,

    Basic,
    Pro,
}
