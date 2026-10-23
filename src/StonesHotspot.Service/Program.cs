using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.WebEncoders;
using Serilog;
using StonesHotspot.Core;
using StonesHotspot.Licensing;
using StonesHotspot.Network;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Web;

// When started by the Service Control Manager the working directory is System32, so point the
// content root at the install folder to find appsettings.json.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

builder.Host.UseWindowsService(options => options.ServiceName = ProductInfo.ServiceName);

// Secrets live in appsettings.Local.json (git-ignored): next to the exe when running from source,
// or in the data folder (%ProgramData%\StonesHotspot) on an installed PC.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
var paths = new AppPaths(builder.Configuration["StonesHotspot:DataDirectory"]).EnsureCreated();
builder.Configuration.AddJsonFile(Path.Combine(paths.Root, "appsettings.Local.json"), optional: true, reloadOnChange: true);

// Admin dashboard (8080) and captive portal (80) — the gate redirects customers' web requests to the portal port.
var ports = ListenPorts.From(builder.Configuration);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    foreach (var port in ports.All)
    {
        if (ports.LocalhostOnly)
            kestrel.ListenLocalhost(port);
        else
            kestrel.ListenAnyIP(port);
    }
});

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File(
        Path.Combine(paths.Logs, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(paths);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<ILicenseService, TrialStubLicenseService>();
    builder.Services.AddHotspotCore(paths.DatabaseFile);
    builder.Services.AddHotspotNetwork(new HotspotNetworkOptions { PortalPort = (ushort)ports.Portal });
    builder.Services.AddHostedService<HotspotEngineService>();
    builder.Services.AddHostedService<DeviceDiscoveryService>();
    builder.Services.AddHotspotWeb();
    // Keys for sign-in cookies and form tokens: kept in the data folder so they survive restarts.
    var keys = builder.Services.AddDataProtection().SetApplicationName("StonesHotspot")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Root, "keys")));
    if (OperatingSystem.IsWindows())
        keys.ProtectKeysWithDpapi(protectToLocalMachine: true);
    // French text as plain UTF-8 instead of &#xE9; entities: lighter pages, same safety.
    builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

    var app = builder.Build();
    await app.InitializeDatabaseAsync();

    app.UseSerilogRequestLogging();
    app.UseHotspotLocalization();
    app.UseCaptivePortal();   // before routing: may send the request to the portal or the "site blocked" page
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapHotspotEndpoints();
    app.MapRazorPages();

    var licence = app.Services.GetRequiredService<ILicenseService>().GetStatus();
    Log.Information("Stone's Hotspot {Version} starting. Data folder: {DataFolder}. Edition: {Edition}",
        HotspotEndpoints.Version, paths.Root, licence.Edition);

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Stone's Hotspot service stopped because of an unexpected error");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
