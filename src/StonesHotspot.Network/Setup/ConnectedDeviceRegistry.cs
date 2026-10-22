namespace StonesHotspot.Network.Setup;

/// <summary>Latest device scan, shared between the discovery loop and the dashboard.</summary>
public sealed class ConnectedDeviceRegistry
{
    private volatile DeviceScanResult _latest = DeviceScanResult.Empty;

    public DeviceScanResult Latest => _latest;

    public void Update(DeviceScanResult result) => _latest = result;
}
