using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Wayfinder.Umbraco.ReferenceApp;

/// <summary>
/// Provisions the demo-recording MCP agent's real backoffice identity on startup — a dedicated
/// <c>Kind = Api</c> user in the admin group, with real client credentials registered against it
/// (<see cref="IBackOfficeUserClientCredentialsManager"/>, the same service
/// <c>CreateClientCredentialsUserController</c> uses). Idempotent, same pattern as
/// <see cref="ReferenceContentSeeder"/>/<see cref="ReferenceBlueprintSeeder"/>.
/// </summary>
/// <remarks>
/// Provisioned here rather than live via Management API calls during the demo recording itself —
/// deliberately, not a shortcut: the historical Umbraco.Prism MCP demo this one is modelled on
/// used the exact same "provisioned once, ahead of time, the same way any integration would be"
/// framing (see <c>docs/demos/licence-transfer-mcp-walkthrough.md</c> in that repo). It also
/// sidesteps a genuine tooling limitation confirmed live in this session: this Playwright version
/// redacts the live Authorization header/token value to the literal string "[redacted]" on every
/// inspection API tried (request.headers(), headerValue(), even raw CDP
/// Network.requestWillBeSent) when the traffic is the *page's own* network activity — so a
/// recording script cannot safely capture the interactive backoffice SPA's own bearer token to
/// make authenticated Management API calls itself. Client-credentials tokens the demo agent mints
/// for *itself* (a call the recording script initiates directly, not one it observes) are
/// unaffected and work exactly as documented in this project's own README.
/// </remarks>
public class ReferenceMcpDemoAgentSeeder(
    IUserService userService,
    IBackOfficeUserClientCredentialsManager clientCredentialsManager,
    IRuntimeState runtimeState,
    ILogger<ReferenceMcpDemoAgentSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string Email = "demo-mcp-agent@wayfinder.local";
    public const string ClientId = "wayfinder-demo-agent";
    public const string ClientSecret = "DemoAgentLocal!12345";

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        var existing = userService.GetByEmail(Email);
        Guid userKey;
        if (existing is not null)
        {
            userKey = existing.Key;
        }
        else
        {
            var createModel = new UserCreateModel
            {
                Email = Email,
                UserName = Email,
                Name = "Demo MCP Agent",
                Kind = UserKind.Api,
                UserGroupKeys = new HashSet<Guid> { Constants.Security.AdminGroupKey }
            };
            var createResult = await userService.CreateAsync(Constants.Security.SuperUserKey, createModel, approveUser: true);
            if (!createResult.Success || createResult.Result.CreatedUser is null)
            {
                logger.LogError("REFERENCE MCP DEMO AGENT SEEDER: failed to create {Email}: {Status}", Email, createResult.Status);
                return;
            }

            userKey = createResult.Result.CreatedUser.Key;
            logger.LogInformation("REFERENCE MCP DEMO AGENT SEEDER: created {Email}.", Email);
        }

        var clientIds = await clientCredentialsManager.GetClientIdsAsync(userKey);
        if (clientIds.Contains(ClientId))
        {
            return;
        }

        var saveResult = await clientCredentialsManager.SaveAsync(userKey, ClientId, ClientSecret);
        if (!saveResult.Success)
        {
            logger.LogError("REFERENCE MCP DEMO AGENT SEEDER: failed to register client credentials for {Email}: {Status}", Email, saveResult.Result);
            return;
        }

        logger.LogInformation("REFERENCE MCP DEMO AGENT SEEDER: registered client credentials for {Email}.", Email);
    }
}
