using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Periodically deletes expired rows from wayfinderServiceRequest. <see cref="UmbracoServiceRequestStore"/>
/// already treats an expired row as a miss on read (lazy expiry), so this sweep exists purely to
/// stop the table growing unbounded from sessions that never come back to expire naturally.
/// </summary>
public sealed class ServiceRequestSweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<ServiceRequestSweepService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        // Wait for the first tick before sweeping — this runs as soon as the host starts, before
        // UmbracoApplicationStartedNotification's migrations (which create the table this sweep
        // targets) have necessarily finished, so an immediate first sweep would race them.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var databaseFactory = scope.ServiceProvider.GetRequiredService<IUmbracoDatabaseFactory>();
                using var db = databaseFactory.CreateDatabase();

                var deleted = db.Execute(
                    "DELETE FROM wayfinderServiceRequest WHERE ExpiresUtc < @0", DateTime.UtcNow);

                if (deleted > 0)
                {
                    logger.LogInformation("Service request sweep removed {Count} expired instance(s).", deleted);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Service request sweep failed; will retry next interval.");
            }
        }
    }
}
