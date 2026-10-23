using StonesHotspot.Licensing.Online;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StonesHotspot.LicenseIssuer;

/// <summary>Uploads licences not yet on the server. Offline: nothing happens, they stay "to sync".</summary>
public sealed class ManagerSync(HttpClient http, Uri server, string apiToken)
{
    public async Task<int> PushAsync(LicenceBook book, CancellationToken ct = default)
    {
        var pending = book.Unsynced();
        if (pending.Count == 0)
            return 0;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, "api/v1/manager/licences"))
        {
            Content = JsonContent.Create(pending.Select(l => new LicenceUpload(l.Id, l.CustomerName, l.MachineId, l.Edition.ToString(), l.IssuedOn, l.ExpiresOn, l.Code, l.RevokedAt))),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        book.MarkSynced(pending.Select(l => l.Id));
        return pending.Count;
    }
}
