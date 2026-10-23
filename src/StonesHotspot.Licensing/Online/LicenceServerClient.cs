using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace StonesHotspot.Licensing.Online;

public sealed record TrialRequest(string MachineId, DateTimeOffset FirstRun, string Nonce, string Version);
/// <param name="ExpiresOn">Expiry of the code this PC has: the server sends a renewal when it knows a later one.</param>
public sealed record CheckInRequest(uint LicenceId, string MachineId, DateOnly ExpiresOn, string Nonce, string Version);
/// <summary>What LicenseManager sends the licence server: new, renewed and revoked licences.</summary>
public sealed record LicenceUpload(uint Id, string Customer, string MachineId, string Edition, DateOnly IssuedOn, DateOnly ExpiresOn, string Code, DateTimeOffset? RevokedAt);

public sealed record ActivateRequest(string Code, string MachineId, string Nonce, string Version);

/// <summary>Talks to the licence server. Every answer is checked by <see cref="LicenseService.ApplyServerAnswer"/>.</summary>
public sealed class LicenceServerClient(HttpClient http, Uri baseAddress, string version)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<(SignedAnswer Answer, string Nonce)> RegisterTrialAsync(string machineId, DateTimeOffset firstRun, CancellationToken ct) =>
        PostAsync("api/v1/trial", nonce => new TrialRequest(machineId, firstRun, nonce, version), ct);

    public Task<(SignedAnswer Answer, string Nonce)> ActivateAsync(string code, string machineId, CancellationToken ct) =>
        PostAsync("api/v1/activate", nonce => new ActivateRequest(code, machineId, nonce, version), ct);

    public Task<(SignedAnswer Answer, string Nonce)> CheckInAsync(uint licenceId, string machineId, DateOnly expiresOn, CancellationToken ct) =>
        PostAsync("api/v1/checkin", nonce => new CheckInRequest(licenceId, machineId, expiresOn, nonce, version), ct);

    /// <summary>The signed latest-version manifest; null when the owner has not published one.</summary>
    public async Task<SignedUpdate?> GetUpdateAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri(baseAddress, "api/v1/update"), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SignedUpdate>(Json, ct);
    }

    private async Task<(SignedAnswer, string)> PostAsync<T>(string path, Func<string, T> body, CancellationToken ct)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var response = await http.PostAsJsonAsync(new Uri(baseAddress, path), body(nonce), Json, ct);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<SignedAnswer>(Json, ct)
                     ?? throw new HttpRequestException("Empty answer from the licence server.");
        return (answer, nonce);
    }
}
