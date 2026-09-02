using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Wayfinder.Umbraco.ReferenceApp;

/// <summary>
/// Creates one root content node on first boot — mirrors
/// <c>UmbracoPrism.TestSite/MobileNavSchemaSetup.cs</c>'s own startup-notification seeding pattern.
/// Two jobs: (1) an empty fresh install has zero published content, which trips uSync.BackOffice's
/// own "no nodes yet" first-boot page — a page this app never ships (that page's view is only
/// shipped by the full <c>uSync</c> package, not the <c>uSync.BackOffice</c> package
/// Wayfinder.Umbraco actually depends on); (2) it gives an editor an actual page to drag the two
/// packaged Wayfinder Block Grid blocks (<c>wayfinderServiceRequestStage</c>/
/// <c>wayfinderServiceRequestWorklist</c> — see <c>Wayfinder.Umbraco/Persistence/CreateServiceRequestStageBlock.cs</c>/
/// <c>CreateServiceRequestWorklistBlock.cs</c>) onto, which is this whole app's reason to exist.
/// </summary>
public class ReferenceContentSeeder(
    IContentTypeService contentTypeService,
    ITemplateService templateService,
    IContentService contentService,
    IDataTypeService dataTypeService,
    IShortStringHelper shortStringHelper,
    IRuntimeState runtimeState,
    ILogger<ReferenceContentSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string HomeAlias = "referenceHome";

    // The two Block Grid data types Wayfinder.Umbraco's own migration plan ships on install.
    private static readonly Guid StageBlockGridDataTypeKey = new("7a3b2d4e-9c5f-4b2a-8d7e-3f8c6b0a2d41");
    private static readonly Guid WorklistBlockGridDataTypeKey = new("9c5d4f60-1e7b-4d4c-af90-5b0e8d2c4f63");

    // Matches CreateServiceRequestStageBlock.cs/CreateServiceRequestWorklistBlock.cs's own fixed
    // element type keys — see this class's own remarks on why the block value is seeded directly
    // rather than left for an editor to place by hand.
    private static readonly Guid StageElementTypeKey = new("6f2a1c3d-8b4e-4a1f-9c6d-2e7b5a9f1c30");
    private static readonly Guid WorklistElementTypeKey = new("8b4c3e5f-0d6a-4c3b-9e8f-4a9d7c1b3e52");

    private static readonly JsonSerializerOptions BlockValueWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        logger.LogInformation("REFERENCE CONTENT SEEDER: starting");

        var homeType = await EnsureHomeDocumentTypeAsync();

        // Three separate pages — a real citizen only ever sees their own apply journey, a real
        // caseworker only ever sees their own queue, and "Home" is a genuine landing page (neither
        // area populated — referenceHome.cshtml renders welcome copy instead of a block for that
        // case), not the citizen page wearing two hats. Co-locating everything on one page was
        // purely a demo shortcut, and it showed: an "Access denied"/empty "Caseworker area" section
        // on the citizen's own page, and "Return to home" from the apply journey looping back to
        // the apply form itself rather than an actual home.
        EnsureContent(homeType, "Home", citizenArea: false, caseworkerArea: false);
        EnsureContent(homeType, "Apply", ReferenceBlueprintSeeder.DefinitionKey, citizenArea: true, caseworkerArea: false);
        EnsureContent(homeType, "Caseworker queue", ReferenceBlueprintSeeder.DefinitionKey, citizenArea: false, caseworkerArea: true);

        // The NJF coaching-register demo (config-only webhook support system, resolved by an
        // Umbraco Automate automation) gets its own citizen and caseworker pages, bound to its
        // own blueprint — the worklist block shows every actionable blueprint's queue for the
        // signed-in persona, so one caseworker page would do, but a dedicated pair keeps the two
        // demos visually separate on the site.
        EnsureContent(homeType, "Apply to coach", ReferenceBlueprintSeeder.CoachingRegisterDefinitionKey, citizenArea: true, caseworkerArea: false);
        EnsureContent(homeType, "Coaching register queue", ReferenceBlueprintSeeder.CoachingRegisterDefinitionKey, citizenArea: false, caseworkerArea: true);

        logger.LogInformation("REFERENCE CONTENT SEEDER: complete");
    }

    private async Task<IContentType> EnsureHomeDocumentTypeAsync()
    {
        if (contentTypeService.Get(HomeAlias) is { } existing)
        {
            return existing;
        }

        var stageBlockGrid = await GetDataTypeOrThrowAsync(StageBlockGridDataTypeKey, "wayfinderServiceRequestStage");
        var worklistBlockGrid = await GetDataTypeOrThrowAsync(WorklistBlockGridDataTypeKey, "wayfinderServiceRequestWorklist");

        var homeType = new ContentType(shortStringHelper, -1)
        {
            Alias = HomeAlias,
            Name = "Reference Home",
            AllowedAsRoot = true,
            Icon = "icon-home"
        };

        const string groupName = "Content";
        homeType.AddPropertyGroup(groupName, "content");

        homeType.AddPropertyType(new PropertyType(shortStringHelper, stageBlockGrid, "citizenArea")
        {
            Name = "Citizen area",
            Description = "Drop a Wayfinder Service Request Stage block here for the citizen-facing journey.",
            Mandatory = false,
            SortOrder = 0
        }, groupName);

        homeType.AddPropertyType(new PropertyType(shortStringHelper, worklistBlockGrid, "caseworkerArea")
        {
            Name = "Caseworker area",
            Description = "Drop a Wayfinder Service Request Worklist block here for the caseworker-facing queue.",
            Mandatory = false,
            SortOrder = 1
        }, groupName);

#pragma warning disable CS0618 // No non-deprecated Save overload on IContentTypeService in v17
        contentTypeService.Save(homeType);
#pragma warning restore CS0618

        await EnsureTemplateAsync(homeType);

        logger.LogInformation("REFERENCE CONTENT SEEDER: created {Alias} document type.", HomeAlias);
        return homeType;
    }

    private async Task<IDataType> GetDataTypeOrThrowAsync(Guid key, string label)
    {
        var dataType = await dataTypeService.GetAsync(key);
        return dataType ?? throw new InvalidOperationException(
            $"Wayfinder.Umbraco's {label} Block Grid data type ({key}) was not found — its migration " +
            "step should have run before this seeder (see WayfinderMigrationPlan).");
    }

    private async Task EnsureTemplateAsync(IContentType contentType)
    {
        var template = await templateService.GetAsync(contentType.Alias);
        if (template == null)
        {
            var attempt = await templateService.CreateForContentTypeAsync(
                contentType.Name!, contentType.Alias, contentType.Alias, Constants.Security.SuperUserKey);
            template = attempt.Result;
        }

        if (template == null)
        {
            logger.LogError("REFERENCE CONTENT SEEDER: failed to create template for {Alias}.", contentType.Alias);
            return;
        }

        contentType.AllowedTemplates = [template];
        contentType.SetDefaultTemplate(template);
#pragma warning disable CS0618
        contentTypeService.Save(contentType);
#pragma warning restore CS0618

        // CreateForContentTypeAsync writes Views/{alias}.cshtml on disk only the first time it
        // runs a boilerplate placeholder — Views/referenceHome.cshtml is committed as real source
        // (same convention as UmbracoPrism.TestSite's own Views/homePage.cshtml) so it's already
        // there on a fresh clone; nothing further to do here.
    }

    private void EnsureContent(IContentType homeType, string name, bool citizenArea, bool caseworkerArea) =>
        EnsureContent(homeType, name, ReferenceBlueprintSeeder.DefinitionKey, citizenArea, caseworkerArea);

    private void EnsureContent(IContentType homeType, string name, string blueprintKey, bool citizenArea, bool caseworkerArea)
    {
        var existing = contentService.GetRootContent().FirstOrDefault(c =>
            c.ContentType.Alias == HomeAlias && string.Equals(c.Name, name, StringComparison.Ordinal));
        if (existing != null)
        {
            return;
        }

        var node = contentService.Create(name, -1, homeType.Alias);

        // Seed a real placed block, not an empty Block Grid — otherwise there is nothing to log in
        // and see: the demo blueprint (reference-demo.json, ReferenceBlueprintSeeder) exists, but a
        // citizen/caseworker persona has no page rendering either block until an editor places one
        // by hand in the backoffice. This is the same BlockGridValue JSON shape Umbraco's own
        // Management API round-trips (confirmed live via GET after a PUT through the real
        // backoffice) — canonical "values" array per content-data item, not the flatter shorthand
        // the PUT endpoint happens to also accept leniently.
        if (citizenArea)
        {
            node.SetValue("citizenArea", BuildStageBlockGridValue(blueprintKey));
        }

        if (caseworkerArea)
        {
            node.SetValue("caseworkerArea", BuildWorklistBlockGridValue());
        }

        contentService.Save(node);
#pragma warning disable CS0618 // No non-obsolete overload of Publish takes a user key on IContentService in v17
        var result = contentService.Publish(node, ["*"], Constants.Security.SuperUserId);
#pragma warning restore CS0618

        if (!result.Success)
        {
            logger.LogError("REFERENCE CONTENT SEEDER: failed to publish {Name} content node: {Result}", name, result.Result);
            return;
        }

        logger.LogInformation("REFERENCE CONTENT SEEDER: created and published {Name} content node.", name);
    }

    private static string BuildStageBlockGridValue(string blueprintKey)
    {
        var blockKey = Guid.NewGuid();
        return BuildBlockGridValueJson(StageElementTypeKey, blockKey,
        [
            new BlockPropertyValue("blueprintKey", "Umbraco.TextBox", blueprintKey)
        ]);
    }

    private static string BuildWorklistBlockGridValue()
    {
        var blockKey = Guid.NewGuid();
        return BuildBlockGridValueJson(WorklistElementTypeKey, blockKey, []);
    }

    private sealed record BlockPropertyValue(string Alias, string EditorAlias, object Value);

    /// <summary>
    /// The persisted <c>Umbraco.BlockGrid</c> property value shape — one block, no areas, full
    /// column span. Matches the exact JSON Umbraco's own Management API returns for a real
    /// backoffice-placed block, not a guessed/simplified shape.
    /// </summary>
    private static string BuildBlockGridValueJson(Guid contentTypeKey, Guid blockKey, IReadOnlyList<BlockPropertyValue> values)
    {
        var blockValue = new
        {
            layout = new Dictionary<string, object>
            {
                ["Umbraco.BlockGrid"] = new[]
                {
                    new
                    {
                        contentKey = blockKey,
                        settingsKey = (Guid?)null,
                        columnSpan = 12,
                        rowSpan = 1,
                        areas = Array.Empty<object>()
                    }
                }
            },
            contentData = new[]
            {
                new
                {
                    contentTypeKey,
                    key = blockKey,
                    values = values.Select(v => new
                    {
                        editorAlias = v.EditorAlias,
                        culture = (string?)null,
                        segment = (string?)null,
                        alias = v.Alias,
                        value = v.Value
                    })
                }
            },
            settingsData = Array.Empty<object>(),
            expose = new[]
            {
                new { contentKey = blockKey, culture = (string?)null, segment = (string?)null }
            }
        };

        return JsonSerializer.Serialize(blockValue, BlockValueWriteOptions);
    }
}
