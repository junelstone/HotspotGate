using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using StonesHotspot.Core;
using StonesHotspot.Licensing;
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

    var app = builder.Build();
    await app.InitializeDatabaseAsync();

    app.UseSerilogRequestLogging();
    app.UseHotspotLocalization();
    app.MapHotspotEndpoints();

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
