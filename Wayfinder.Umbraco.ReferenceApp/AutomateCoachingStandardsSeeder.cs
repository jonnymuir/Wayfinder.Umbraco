using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Automate.Core.Automations;
using Umbraco.Automate.Core.Conditions;
using Umbraco.Automate.Core.Workspaces;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Wayfinder.Umbraco.ReferenceApp;

/// <summary>
/// Seeds the "NJF Coaching Standards" Umbraco Automate automation, so the config-only webhook
/// support system (<c>appsettings.json</c> -> <c>Wayfinder:SupportSystems</c>) has a real
/// automation ready and waiting, with no manual canvas build. Built entirely in code via
/// <see cref="IAutomationService"/>: a webhook trigger, an If branch on the applicant's own data,
/// an email to the standards officer, a human approval, and a <see cref="ResolveSupportSystemOutcomeAction"/>
/// step per outcome.
/// <para/>
/// Runs as a background service, not a startup notification handler: Automate has no default
/// workspace on a fresh install and stands one up lazily, so this polls (and creates one itself
/// if needed) before seeding. Create-or-update then publish on every boot, so a stale automation
/// signing with a previous run's key is refreshed. Never throws: a demo automation failing to
/// seed must not stop the site.
/// </summary>
public sealed class AutomateCoachingStandardsSeeder(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IRuntimeState runtimeState,
    ILogger<AutomateCoachingStandardsSeeder> logger)
    : BackgroundService
{
    // Must match the automationId in appsettings.json's Wayfinder:SupportSystems[0].endpoint.url.
    private static readonly Guid AutomationId = new("6f1c0000-0000-0000-0000-00000000c0de");

    private const string StandardsOfficerEmail = "standards-officer@example.test";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait for Umbraco to reach Run (past unattended install) and for Automate's own
            // schema migration to finish, then seed (creating a workspace first if none exists).
            for (var attempt = 0; attempt < 60 && !stoppingToken.IsCancellationRequested; attempt++)
            {
                if (runtimeState.Level == RuntimeLevel.Run && await TrySeedAsync(stoppingToken))
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }

            logger.LogWarning(
                "AUTOMATE COACHING STANDARDS SEEDER: gave up. Build the automation by hand per " +
                "docs/automate-support-system-walkthrough.md.");
        }
        catch (OperationCanceledException)
        {
            // Host shutting down.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AUTOMATE COACHING STANDARDS SEEDER: could not seed the automation.");
        }
    }

    private async Task<bool> TrySeedAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var workspaceService = scope.ServiceProvider.GetRequiredService<IWorkspaceService>();
        var automationService = scope.ServiceProvider.GetRequiredService<IAutomationService>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        var signingKey = configuration["NJF_STANDARDS_SIGNING_KEY"];


        // Automate has no default workspace on a fresh install; an automation must belong to one,
        // and publishing requires the workspace to have a service-account user with the sections
        // its trigger and actions need. Reuse the first workspace if a human already made one,
        // otherwise stand up a plain one whose service account is this reference app's own
        // unattended admin (an Administrator, so it has every section — a deliberate shortcut for
        // a single-tenant demo, not a pattern for a real multi-user host).
        Workspace workspace;
        try
        {
            var (workspaces, _) = await workspaceService.GetWorkspacesPagedAsync(take: 1, cancellationToken: ct);
            workspace = workspaces.FirstOrDefault()
                ?? await workspaceService.CreateWorkspaceAsync(
                    new Workspace { Alias = "default", Name = "Default" }, cancellationToken: ct);

            if (workspace.ServiceAccountKey == Guid.Empty)
            {
                var adminEmail = configuration["Umbraco:CMS:Unattended:UnattendedUserEmail"] ?? "admin@example.test";
                var admin = userService.GetByEmail(adminEmail);
                if (admin is null)
                {
                    return false;
                }

                workspace.ServiceAccountKey = admin.Key;
                await workspaceService.UpdateWorkspaceAsync(workspace, cancellationToken: ct);
            }
        }
        catch
        {
            // Automate's own schema/services not ready yet — try again next tick.
            return false;
        }

        var automation = BuildAutomation(workspace.Id, signingKey);

        var existing = await automationService.GetAutomationAsync(AutomationId, ct);
        if (existing is null)
        {
            await automationService.CreateAutomationAsync(automation, cancellationToken: ct);
            logger.LogInformation("AUTOMATE COACHING STANDARDS SEEDER: created automation {Id}.", AutomationId);
        }
        else
        {
            existing.Name = automation.Name;
            existing.Description = automation.Description;
            existing.WorkspaceId = automation.WorkspaceId;
            existing.Trigger = automation.Trigger;
            existing.Steps = automation.Steps;
            existing.Connections = automation.Connections;
            await automationService.UpdateAutomationAsync(existing, cancellationToken: ct);
            logger.LogInformation("AUTOMATE COACHING STANDARDS SEEDER: refreshed automation {Id}.", AutomationId);
        }

        await automationService.PublishAutomationAsync(AutomationId, cancellationToken: ct);
        logger.LogInformation("AUTOMATE COACHING STANDARDS SEEDER: published automation {Id}.", AutomationId);
        return true;
    }

    private static Automation BuildAutomation(Guid workspaceId, string? signingKey)
    {
        var checkExperience = Guid.NewGuid();
        var autoAccredit = Guid.NewGuid();
        var emailOfficer = Guid.NewGuid();
        var requestApproval = Guid.NewGuid();
        var resolveProvisional = Guid.NewGuid();
        var resolveReferred = Guid.NewGuid();

        // Resolve the invocation via the in-process ResolveSupportSystemOutcomeAction rather than
        // an HTTP Request step: Automate's built-in HttpRequestAction blocks loopback (SSRF
        // protection), so an automation on the same box as Wayfinder cannot call the site back.
        static StepConfiguration Resolve(Guid id, string name, string outcome, string note, double x) => new()
        {
            Id = id,
            ActionAlias = "wayfinder.resolveSupportSystemOutcome",
            Name = name,
            Position = new StepPosition { X = x, Y = 480 },
            Settings = new()
            {
                ["invocationId"] = "${trigger.body.invocationId}",
                ["outcomeKey"] = outcome,
                ["resultPayload"] = $$"""
                    {"coachingStandardsOutcome":"{{outcome}}","coachingStandardsNote":"{{note}}"}
                    """,
            },
        };

        var steps = new List<StepConfiguration>
        {
            new()
            {
                Id = checkExperience,
                ActionAlias = "umbracoAutomate.if",
                Name = "Meets the fast-track criteria?",
                Position = new StepPosition { X = 0, Y = 0 },
                Settings = new()
                {
                    ["conditions"] = new ConditionSet
                    {
                        Groups =
                        [
                            new ConditionGroup
                            {
                                Conditions =
                                [
                                    new Condition
                                    {
                                        LeftOperand = "${trigger.body.inputs.yearsCoaching}",
                                        Operator = ConditionOperator.GreaterThanOrEquals,
                                        RightOperand = "2",
                                    },
                                    new Condition
                                    {
                                        LeftOperand = "${trigger.body.inputs.disclosureReference}",
                                        Operator = ConditionOperator.IsNotEmpty,
                                        RightOperand = "",
                                    },
                                ],
                            },
                        ],
                    },
                },
            },
            Resolve(autoAccredit, "Resolve: accredited (auto)", "accredited",
                "Auto-accredited: two or more years' experience and a safeguarding disclosure reference on file.", -200),
            new()
            {
                Id = emailOfficer,
                ActionAlias = "umbracoAutomate.sendEmail",
                Name = "Email the standards officer",
                Position = new StepPosition { X = 200, Y = 160 },
                Settings = new()
                {
                    ["to"] = StandardsOfficerEmail,
                    ["subject"] = "Coaching register application needs review",
                    ["isHtml"] = false,
                    ["body"] = "An application to join the NJF coaching register needs a human decision. Applicant: ${trigger.body.inputs.applicantName}. Years coaching: ${trigger.body.inputs.yearsCoaching}. Review it in the Automate section under Pending approvals.",
                },
            },
            new()
            {
                Id = requestApproval,
                ActionAlias = "umbracoAutomate.requestApproval",
                Name = "Accredit, provisional, or refer?",
                Position = new StepPosition { X = 200, Y = 320 },
                Settings = new()
                {
                    ["prompt"] = "Approve to mark this coach 'provisional'; reject to 'refer'. Applicant: ${trigger.body.inputs.applicantName}.",
                    ["timeoutHours"] = 72,
                },
            },
            Resolve(resolveProvisional, "Resolve: provisional", "provisional",
                "Provisionally accredited by the NJF standards officer. Complete a mentored session within 6 months.", 80),
            Resolve(resolveReferred, "Resolve: referred", "referred",
                "Referred to the NJF safeguarding lead. The applicant will be contacted directly.", 320),
        };

        var connections = new List<StepConnection>
        {
            // The trigger is step Guid.Empty in the graph; the compiler BFSes from it to find the
            // entry step, so the first real step must be wired to it or nothing is reachable.
            new() { SourceStepId = Guid.Empty, TargetStepId = checkExperience },
            new() { SourceStepId = checkExperience, TargetStepId = autoAccredit, Outcome = "true" },
            new() { SourceStepId = checkExperience, TargetStepId = emailOfficer, Outcome = "false" },
            new() { SourceStepId = emailOfficer, TargetStepId = requestApproval },
            new() { SourceStepId = requestApproval, TargetStepId = resolveProvisional, Outcome = "approved" },
            new() { SourceStepId = requestApproval, TargetStepId = resolveReferred, Outcome = "rejected" },
        };

        var trigger = new TriggerConfiguration
        {
            TriggerAlias = "umbracoAutomate.webhook",
            Settings = new()
            {
                ["allowedMethod"] = "POST",
                // A signed webhook when the AppHost supplies a key; otherwise a plain shared
                // secret equal to the key name's absence is meaningless, so fall back to an
                // unauthenticated webhook for a bare `dotnet run` (trusted-loopback demo only).
                ["authenticator"] = string.IsNullOrEmpty(signingKey)
                    ? new Dictionary<string, object?> { ["alias"] = "plain-secret", ["settings"] = new Dictionary<string, object?> { ["secret"] = "" } }
                    : new Dictionary<string, object?> { ["alias"] = "hmac-sha256", ["settings"] = new Dictionary<string, object?> { ["signingKey"] = signingKey } },
            },
        };

        return new Automation
        {
            Id = AutomationId,
            Alias = "njf-coaching-standards",
            Name = "NJF Coaching Standards",
            Description = "Resolves the njf-coaching-standards support system (appsettings.json Wayfinder:SupportSystems). Seeded by AutomateCoachingStandardsSeeder.",
            Status = AutomationStatus.Draft,
            WorkspaceId = workspaceId,
            Trigger = trigger,
            Steps = steps,
            Connections = connections,
        };
    }
}
