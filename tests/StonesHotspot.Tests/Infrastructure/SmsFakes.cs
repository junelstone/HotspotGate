using System.Collections.Concurrent;
using System.Text;
using StonesHotspot.Core.Security;
using StonesHotspot.Core.Sms;

namespace StonesHotspot.Tests.Infrastructure;

/// <summary>Reversible "encryption" for tests: visibly different from the plain value.</summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plain) => "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

    public string? Unprotect(string protectedValue) =>
        protectedValue.StartsWith("enc:", StringComparison.Ordinal) ? Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[4..])) : null;
}

/// <summary>Keeps the SMS "sent" instead of sending them; can be told to fail.</summary>
public sealed class FakeSmsTransportFactory : ISmsTransportFactory, ISmsTransport
{
    public ConcurrentQueue<(string Phone, string Text)> Sent { get; } = new();
    public string? FailWith { get; set; }

    public ISmsTransport Create(SmsSettings settings) => this;

    public IReadOnlyList<string> SerialPorts() => ["COM3"];

    public Task SendAsync(string phone, string text, CancellationToken ct)
    {
        if (FailWith is not null)
            throw new SmsException(FailWith);
        Sent.Enqueue((phone, text));
        return Task.CompletedTask;
    }

    /// <summary>The 6-digit code of the last SMS sent to <paramref name="phone"/>.</summary>
    public string LastCode(string phone) =>
        System.Text.RegularExpressions.Regex.Match(Sent.Last(s => s.Phone == phone).Text, @"\b(\d{6})\b").Groups[1].Value;
}
