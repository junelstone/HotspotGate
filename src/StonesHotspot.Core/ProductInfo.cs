namespace StonesHotspot.Core;

public static class ProductInfo
{
    /// <summary>Windows service name (sc.exe / services.msc), shared by the service and the tray app.</summary>
    public const string ServiceName = "StonesHotspot";

    public const int DefaultAdminPort = 8080;

    /// <summary>Product name written in generated files (PDF producer, printed footers).</summary>
    public const string DisplayName = "Stone's Hotspot";
}
