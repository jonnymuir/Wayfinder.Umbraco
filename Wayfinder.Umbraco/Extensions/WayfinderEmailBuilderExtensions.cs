using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Extensions;
using Wayfinder.Umbraco.Emails;
using Wayfinder.Umbraco.Services;
using Wayfinder.Umbraco.Visitors;

namespace Wayfinder.Umbraco.Extensions;

/// <summary>
/// Emails a service sends as its requests move on: a reply to the person who wrote in, with a private link
/// back to their request; a note to the team that something is waiting. Each is a Razor template, sent
/// through Umbraco's own email sender (its <c>Umbraco:CMS:Global:Smtp</c> settings) in the background.
/// </summary>
public static class WayfinderEmailBuilderExtensions
{
    public static IUmbracoBuilder AddWayfinderEmails(this IUmbracoBuilder builder, Action<WayfinderEmailOptions> configure)
    {
        builder.Services.AddOptions<WayfinderEmailOptions>()
            .Configure(configure)
            .Validate(o => MailAddress.TryCreate(o.From, out _), "Wayfinder emails need a From address.")
            .Validate(o => Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "Wayfinder emails need PublicBaseUrl, the site's absolute https address.")
            .Validate(o => o.Emails.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() == o.Emails.Count,
                "Each Wayfinder email needs a name of its own.")
            .Validate(o => o.Emails.All(e => e.ToField is not null || e.ToAddresses.Count > 0),
                "Each Wayfinder email needs someone to go to: ToField, ToAddresses, or both.")
            .Validate(o => o.Emails.All(e => e.PrivateLinkPagePath is null || WayfinderVisitors.IsLocalPath(e.PrivateLinkPagePath)),
                "A Wayfinder email's PrivateLinkPagePath must be a path on this site, such as \"/contact-us/\".")
            .ValidateOnStart();

        builder.Services.TryAddSingleton<IWayfinderEmailQueue, BackgroundTaskEmailQueue>();
        builder.Services.TryAddTransient<RazorEmailRenderer>();
        builder.Services.TryAddTransient<WayfinderEmailSender>();
        builder.AddNotificationAsyncHandler<ServiceRequestTransitionedNotification, TransitionEmailHandler>();
        return builder;
    }
}
