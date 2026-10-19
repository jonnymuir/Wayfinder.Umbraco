using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.Persistence;
using Wayfinder.Umbraco.Configuration;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Periodically deletes expired rows from wayfinderServiceRequest. <see cref="UmbracoServiceRequestStore"/>
/// already treats an expired row as a miss on read (lazy expiry), so this sweep exists purely to
/// stop the table growing unbounded from sessions that never come back to expire naturally. It
/// also removes audit events older than <see cref="WayfinderServiceDesignOptions.AuditRetention"/>.
/// </summary>
public sealed class ServiceRequestSweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WayfinderServiceDesignOptions> options,
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

                SweepAuditEvents(scope.ServiceProvider);
                SweepVisitorDevices(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Service request sweep failed; will retry next interval.");
            }
        }
    }

    // A visitor's browser that hasn't been used for longer than its cookie lasts can never come back.
    private void SweepVisitorDevices(IServiceProvider services)
    {
        if (services.GetService<Visitors.IVisitorDeviceStore>() is not { } devices
            || services.GetService<IOptions<Visitors.WayfinderVisitorOptions>>() is not { } visitorOptions)
        {
            return;
        }

        var removed = devices.RemoveUnusedSince(DateTime.UtcNow - visitorOptions.Value.CookieLifetime);
        if (removed > 0)
        {
            logger.LogInformation("Visitor sweep removed {Count} browser(s) unused for longer than their cookie lasts.", removed);
        }
    }

    // Only the package's own store: a host that registered its own IAuditLogStore owns its retention.
    private void SweepAuditEvents(IServiceProvider services)
    {
        var retention = options.CurrentValue.AuditRetention;
        if (retention <= TimeSpan.Zero || services.GetService<Wayfinder.Engine.Abstractions.IAuditLogStore>() is not UmbracoAuditLogStore store)
        {
            return;
        }

        var removed = store.RemoveOlderThan(DateTimeOffset.UtcNow - retention);
        if (removed > 0)
        {
            logger.LogInformation("Audit sweep removed {Count} event(s) older than {Retention}.", removed, retention);
        }
    }
}
