namespace StonesHotspot.Network.Ics;

public enum IcsRole { Public, Private }

/// <summary>An adapter currently taking part in Windows Internet Connection Sharing.</summary>
public sealed record IcsShare(string AdapterId, string Name, IcsRole Role);

public sealed record IcsStatus(bool Supported, IReadOnlyList<IcsShare> Shares)
{
    public IcsShare? Public => Shares.FirstOrDefault(s => s.Role == IcsRole.Public);
    public IcsShare? Private => Shares.FirstOrDefault(s => s.Role == IcsRole.Private);

    public bool IsSharing(string wanId, string lanId) =>
        string.Equals(Public?.AdapterId, wanId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Private?.AdapterId, lanId, StringComparison.OrdinalIgnoreCase);

    public static IcsStatus NotSupported { get; } = new(false, []);
}

/// <summary>Thrown when Windows refuses an ICS change; the message is shown to the admin.</summary>
public sealed class IcsException(string message, Exception? inner = null) : Exception(message, inner);

public interface IIcsController
{
    IcsStatus GetStatus();

    /// <summary>Shares the internet of <paramref name="wanId"/> to <paramref name="lanId"/>, replacing any other sharing.</summary>
    void Enable(string wanId, string lanId);

    /// <summary>Turns sharing off on every adapter.</summary>
    void Disable();
}

/// <summary>Used on non-Windows systems (development, tests).</summary>
public sealed class UnsupportedIcsController : IIcsController
{
    public IcsStatus GetStatus() => IcsStatus.NotSupported;
    public void Enable(string wanId, string lanId) => throw new PlatformNotSupportedException("ICS exists only on Windows.");
    public void Disable() { }
}
