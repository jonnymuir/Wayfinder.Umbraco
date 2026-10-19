using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.HostedServices;

namespace Wayfinder.Umbraco.Emails;

/// <summary>
/// Where emails wait to be sent, so a slow or unreachable mail server never holds up the person whose
/// move sent one. The default sends them in the background in this process; a site that wants retries
/// that survive a restart registers its own (a Hangfire job calling <see cref="WayfinderEmailSender.SendAsync"/>).
/// </summary>
public interface IWayfinderEmailQueue
{
    void Enqueue(WayfinderEmailJob job);
}

/// <summary>Sends queued emails on Umbraco's background task queue. A failed send is logged, not retried.</summary>
public sealed class BackgroundTaskEmailQueue(
    IBackgroundTaskQueue queue,
    IServiceScopeFactory scopes,
    ILogger<BackgroundTaskEmailQueue> logger) : IWayfinderEmailQueue
{
    public void Enqueue(WayfinderEmailJob job) =>
        queue.QueueBackgroundWorkItem(async cancellationToken =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<WayfinderEmailSender>().SendAsync(job, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not send the {EmailName} email for request {InstanceId}.", job.EmailName, job.InstanceId);
            }
        });
}
