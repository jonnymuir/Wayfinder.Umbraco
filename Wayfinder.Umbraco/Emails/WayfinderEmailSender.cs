using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.Emails;

/// <summary>Sends one queued email: renders its template for the request and sends it through Umbraco's email sender.</summary>
public sealed partial class WayfinderEmailSender(
    IOptions<WayfinderEmailOptions> options,
    IServiceRequestStore requests,
    RazorEmailRenderer renderer,
    IEmailSender emailSender,
    IServiceProvider services,
    ILogger<WayfinderEmailSender> logger)
{
    public async Task SendAsync(WayfinderEmailJob job, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var email = settings.Emails.SingleOrDefault(e => e.Name == job.EmailName)
            ?? throw new InvalidOperationException($"No Wayfinder email is named '{job.EmailName}'.");

        if (!requests.TryGet(job.InstanceId, out var request) || request.BlueprintKey != email.BlueprintKey)
        {
            RequestGone(logger, job.InstanceId, job.EmailName);
            return;
        }

        var recipients = Recipients(email, request.FieldValues.GetValueOrDefault(email.ToField ?? "")?.ToString());
        if (recipients.Count == 0)
        {
            NoValidRecipient(logger, job.EmailName, job.InstanceId);
            return;
        }

        var model = new WayfinderEmailModel(
            request, job.Action, job.ConversationLengths, settings.PublicBaseUrl, PrivateLink(email, request.InstanceId, settings.PublicBaseUrl),
            services.GetService<IOptions<WayfinderVisitorOptions>>()?.Value.PrivateLinkLifetime ?? TimeSpan.Zero);
        var body = await renderer.RenderAsync(email.Template, model);

        foreach (var recipient in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await emailSender.SendAsync(
                new EmailMessage(from: settings.From, to: recipient, subject: email.Subject, body: body, isBodyHtml: true),
                $"Wayfinder.{email.Name}",
                enableNotification: false,
                expires: TimeSpan.FromDays(1));
        }

        EmailSent(logger, job.EmailName, job.InstanceId);
    }

    // Only well-formed single addresses: a field's value is whatever someone typed into a form.
    private static List<string> Recipients(WayfinderEmail email, string? fieldAddress) =>
        email.ToAddresses
            .Append(fieldAddress)
            .Where(address => !string.IsNullOrWhiteSpace(address)
                && MailAddress.TryCreate(address, out var parsed)
                && string.Equals(parsed.Address, address.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(address => address!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string? PrivateLink(WayfinderEmail email, string instanceId, string publicBaseUrl)
    {
        if (email.PrivateLinkPagePath is not { } pagePath)
        {
            return null;
        }

        var links = services.GetService<WayfinderPrivateLinks>()
            ?? throw new InvalidOperationException($"The {email.Name} email asks for a private link, which needs AddWayfinderVisitors.");
        return links.Issue(publicBaseUrl, instanceId, pagePath);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request {InstanceId} no longer exists; the {EmailName} email was not sent.")]
    private static partial void RequestGone(ILogger logger, string instanceId, string emailName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {EmailName} email for request {InstanceId} has no valid address to go to.")]
    private static partial void NoValidRecipient(ILogger logger, string emailName, string instanceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent the {EmailName} email for request {InstanceId}.")]
    private static partial void EmailSent(ILogger logger, string emailName, string instanceId);
}
