using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using StonesHotspot.LicenseIssuer;
using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Codes;
using StonesHotspot.Licensing.Online;

namespace StonesHotspot.Tests;

/// <summary>
/// The Supabase licence server (licence-server/, TypeScript) must speak exactly the protocol of the C# hotspot:
/// keys and codes are made here with the owner's tools, the TypeScript handler runs in Node (in memory), and every
/// answer is checked with the hotspot's own verification code. Needs Node 22.6+; without it the test does nothing.
/// </summary>
public sealed class SupabaseLicenceServerTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "StonesHotspot.sln")))
            dir = dir.Parent!;
        return dir.FullName;
    }

    /// <summary>Node able to run .ts files (type stripping), or null.</summary>
    private static string? Node()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true })!;
            var version = process.StandardOutput.ReadToEnd().Trim().TrimStart('v').Split('.');
            process.WaitForExit();
            return int.Parse(version[0]) > 22 || int.Parse(version[0]) == 22 && int.Parse(version[1]) >= 6 ? "node" : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static JsonObject Run(string node, object input)
    {
        var start = new ProcessStartInfo(node) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Root() };
        foreach (var arg in new[] { "--experimental-strip-types", "--no-warnings", Path.Combine("licence-server", "tests", "compat.ts") })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Write(JsonSerializer.Serialize(input));
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
        return JsonNode.Parse(output)!.AsObject();
    }

    private static int Status(JsonObject results, string name) => results[name]!["status"]!.GetValue<int>();

    private static ServerAnswer Answer(JsonObject results, string name, byte[] serverPublic, string nonce, string machineId)
    {
        Assert.Equal(200, Status(results, name));
        var signed = SignedAnswer.FromJson(results[name]!["body"]!.ToJsonString())!;
        var answer = signed.Verify(serverPublic);
        Assert.NotNull(answer);                       // signature accepted by the hotspot
        Assert.Equal(nonce, answer.Nonce);            // bound to the request
        Assert.Equal(machineId, answer.MachineId);    // what LicenseService.ApplyServerAnswer compares
        return answer;
    }

    [Fact]
    public void Supabase_server_answers_are_accepted_by_the_hotspot()
    {
        if (Node() is not { } node)
            return;

        var keys = KeyVault.Generate();
        var machine = MachineId.FromRaw("guid-1", "board-1", "disk-1", "cpu-1");
        var other = MachineId.FromRaw("guid-2", "board-2", "disk-2", "cpu-2");
        var licence = new LicenceData(4711, Edition.Pro, new DateOnly(2026, 3, 1), new DateOnly(2027, 3, 1), machine, "Cyber Café Étoile");
        var code = LicenceCode.Create(licence, keys.LicencePrivate);
        var renewed = licence with { IssuedOn = new DateOnly(2027, 2, 20), ExpiresOn = new DateOnly(2028, 3, 1) };
        var renewedCode = LicenceCode.Create(renewed, keys.LicencePrivate);
        var installer = Convert.ToHexString(SHA256.HashData("installer"u8)).ToLowerInvariant();

        object Upload(LicenceData l, string c, DateTimeOffset? revokedAt) =>
            new LicenceUpload(l.Id, l.Customer, l.MachineId.ToString(), l.Edition.ToString(), l.IssuedOn, l.ExpiresOn, c, revokedAt);
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);   // what LicenseManager sends
        var results = Run(node, new
        {
            serverPrivateKey = Convert.ToBase64String(keys.ServerPrivate),
            licencePublicKey = Convert.ToBase64String(keys.LicencePublic),
            managerToken = "token-123",
            machineId = machine.ToString(),
            otherMachineId = other.ToString(),
            code,
            renewedCode,
            renewedUpload = JsonNode.Parse(JsonSerializer.Serialize(Upload(renewed, renewedCode, null), web)),
            revocationUpload = JsonNode.Parse(JsonSerializer.Serialize(Upload(renewed, renewedCode, new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)), web)),
            manifest = new { version = "1.2.0", url = "https://github.com/junelstone/stoneshotspot-releases/releases/download/v1.2.0/StonesHotspot-Setup-1.2.0.exe", sha256 = installer, notes = "Corrections", publishedAt = "2026-10-23T10:00:00Z" },
        });
        var mid = machine.ToString();

        Assert.Equal(200, Status(results, "health"));

        var trial = Answer(results, "trial", keys.ServerPublic, "n-trial", mid);
        Assert.Equal(ServerStatus.TrialRegistered, trial.Status);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero), trial.TrialFirstRun);
        // A reinstall later does not restart the trial: the earliest first run is kept.
        Assert.Equal(trial.TrialFirstRun, Answer(results, "trialLater", keys.ServerPublic, "n-trial2", mid).TrialFirstRun);
        Assert.Equal(400, Status(results, "badTrial"));

        var activated = Answer(results, "activate", keys.ServerPublic, "n-activate", mid);
        Assert.Equal(ServerStatus.Valid, activated.Status);
        Assert.Equal(4711u, activated.LicenceId);
        Assert.Equal(400, Status(results, "tampered"));   // a changed character breaks the licence signature

        Assert.Equal(ServerStatus.Unknown, Answer(results, "otherPc", keys.ServerPublic, "n-other", other.ToString()).Status);

        Assert.Equal(401, Status(results, "uploadWrongToken"));
        Assert.Equal(200, Status(results, "upload"));
        var renewal = Answer(results, "renewed", keys.ServerPublic, "n-renew", mid);
        Assert.Equal(ServerStatus.Renewed, renewal.Status);
        Assert.Equal(new DateOnly(2028, 3, 1), LicenceCode.Verify(renewal.RenewedCode, keys.LicencePublic)?.ExpiresOn);
        Assert.Equal(ServerStatus.Renewed, Answer(results, "renewalGet", keys.ServerPublic, "n-get", mid).Status);

        Assert.Equal(200, Status(results, "revoke"));
        var revoked = Answer(results, "revoked", keys.ServerPublic, "n-revoked", mid);
        Assert.Equal(ServerStatus.Revoked, revoked.Status);
        Assert.Equal(new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero), revoked.RevokedAt);

        Assert.Equal(204, Status(results, "noUpdate"));
        var update = new SignedUpdate(results["update"]!["body"]!["payload"]!.GetValue<string>(), results["update"]!["body"]!["signature"]!.GetValue<string>())
            .Verify(keys.ServerPublic);
        Assert.Equal("1.2.0", update?.Version);
        Assert.Equal(installer, update?.Sha256);
        Assert.True(update!.IsNewerThan("1.1.0"));

        Assert.Equal(404, Status(results, "unknownPath"));
        Assert.Equal(7, results["checkinCount"]!.GetValue<int>());   // 2 trials, activation, 4 licence check-ins
    }
}
