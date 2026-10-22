using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace StonesHotspot.Tests;

public sealed class ServiceSmokeTests : IClassFixture<ServiceSmokeTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public string DataDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "StonesHotspotTests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("StonesHotspot:DataDirectory", DataDirectory);
    }

    private readonly Factory _factory;

    public ServiceSmokeTests(Factory factory) => _factory = factory;

    private sealed record Health(string Status, string Edition, int? DaysLeft);

    [Fact]
    public async Task Health_endpoint_reports_ok_and_trial()
    {
        var health = await _factory.CreateClient().GetFromJsonAsync<Health>("/health");

        Assert.Equal("ok", health?.Status);
        Assert.Equal("Trial", health?.Edition);
        Assert.Equal(30, health?.DaysLeft);
    }

    [Theory]
    [InlineData("", "lang=\"fr\"", "Version d'essai Pro")]
    [InlineData("?lang=en", "lang=\"en\"", "Pro trial")]
    public async Task Admin_placeholder_is_localised(string query, string langAttribute, string text)
    {
        var response = await _factory.CreateClient().GetAsync("/admin" + query);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(langAttribute, html);
        Assert.Contains(text, html);
    }

    [Fact]
    public async Task Service_creates_its_data_and_log_folders()
    {
        await _factory.CreateClient().GetAsync("/health");

        Assert.True(Directory.Exists(Path.Combine(_factory.DataDirectory, "logs")));
        Assert.True(Directory.Exists(Path.Combine(_factory.DataDirectory, "backups")));
    }
}
