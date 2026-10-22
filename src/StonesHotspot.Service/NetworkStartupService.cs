using StonesHotspot.Network.Setup;

namespace StonesHotspot.Service;

/// <summary>At service start, switches internet sharing back on if Windows dropped it (reboot, update).</summary>
internal sealed class NetworkStartupService(IServiceScopeFactory scopes, ILogger<NetworkStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().EnsureAppliedAsync(cancellationToken);
            if (result is null)
                return;
            if (result.Succeeded)
                logger.LogInformation("Internet sharing was not active and has been switched back on");
            else
                logger.LogError("Could not switch internet sharing back on: {Problems}",
                    string.Join("; ", result.Problems.Select(p => $"{p.Code} {p.Detail}")));
        }
        catch (Exception ex)
        {
            // Never stop the service because of the network: the dashboard must stay reachable to fix it.
            logger.LogError(ex, "Network start-up check failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
