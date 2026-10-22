using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Services;

namespace StonesHotspot.Service;

internal static class DatabaseStartup
{
    /// <summary>Migrates and seeds the database, then restores sessions that were active before the restart.</summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DatabaseInitializer>>();
        var paths = scope.ServiceProvider.GetRequiredService<AppPaths>();

        var result = await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        if (result.InitialAdminPassword is { } password)
        {
            var file = InitialAdminPasswordFile.Write(paths, DatabaseInitializer.AdminUsername, password);
            logger.LogWarning("Admin account created. Its first password is in {File} (must be changed at first login)", file);
        }

        var sessions = await scope.ServiceProvider.GetRequiredService<SessionService>().ResumeAfterRestartAsync();
        logger.LogInformation("Database ready at {Database}; {Count} active session(s) restored", paths.DatabaseFile, sessions.Count);
    }
}
