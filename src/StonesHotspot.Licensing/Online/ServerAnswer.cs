using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StonesHotspot.Licensing.Codes;

namespace StonesHotspot.Licensing.Online;

public static class ServerStatus
{
    public const string Valid = "valid";
    public const string Revoked = "revoked";
    public const string Renewed = "renewed";
    public const string Unknown = "unknown";
    public const string TrialRegistered = "trial-registered";
}

/// <summary>What the licence server says about a licence or trial, signed with the server key.</summary>
/// <param name="Nonce">Echo of the request's random value: an old answer cannot be replayed.</param>
/// <param name="RenewedCode">With <see cref="ServerStatus.Renewed"/>: the new licence code.</param>
public sealed record ServerAnswer(
    string Status, uint? LicenceId, string MachineId, string Nonce, DateTimeOffset ServerTime,
    DateTimeOffset? RevokedAt = null, string? RenewedCode = null, DateTimeOffset? TrialFirstRun = null);

/// <summary>The signed form of an answer: base64 JSON payload + Ed25519 signature.</summary>
public sealed record SignedAnswer(
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("signature")] string Signature)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SignedAnswer Sign(ServerAnswer answer, byte[] serverPrivateKey)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(answer, Json));
        return new SignedAnswer(Convert.ToBase64String(payload), Convert.ToBase64String(Ed25519.Sign(serverPrivateKey, payload)));
    }

    /// <summary>The answer when the signature is the server's; null otherwise.</summary>
    public ServerAnswer? Verify(byte[] serverPublicKey)
    {
        try
        {
            var payload = Convert.FromBase64String(Payload);
            return Ed25519.Verify(serverPublicKey, payload, Convert.FromBase64String(Signature))
                ? JsonSerializer.Deserialize<ServerAnswer>(payload, Json)
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static SignedAnswer? FromJson(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<SignedAnswer>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
