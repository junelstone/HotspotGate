namespace StonesHotspot.Network.Setup;

public interface IPrivilegeChecker
{
    /// <summary>Running elevated (Administrator, or LocalSystem for the service).</summary>
    bool IsAdministrator { get; }

    bool IsWindows { get; }
}

public sealed class ProcessPrivilegeChecker : IPrivilegeChecker
{
    public bool IsAdministrator => Environment.IsPrivilegedProcess;
    public bool IsWindows => OperatingSystem.IsWindows();
}
