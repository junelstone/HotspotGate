using Serilog;

// Online licence API (trial registration, activation, daily check-in, revocation).
// Phase 0: health endpoint only. Hosting target to be decided with the owner before deployment work.
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Services.AddSerilog((_, lc) => lc.ReadFrom.Configuration(builder.Configuration));

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();
