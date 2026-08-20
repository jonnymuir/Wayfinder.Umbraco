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

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        logger.LogInformation("REFERENCE CONTENT SEEDER: starting");

        var homeType = await EnsureHomeDocumentTypeAsync();
        await EnsureHomeContentAsync(homeType);

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

    private async Task EnsureHomeContentAsync(IContentType homeType)
    {
        var existing = contentService.GetRootContent().FirstOrDefault(c => c.ContentType.Alias == HomeAlias);
        if (existing != null)
        {
            return;
        }

        var home = contentService.Create("Home", -1, homeType.Alias);
        contentService.Save(home);
#pragma warning disable CS0618 // No non-obsolete overload of Publish takes a user key on IContentService in v17
        var result = contentService.Publish(home, ["*"], Constants.Security.SuperUserId);
#pragma warning restore CS0618

        if (!result.Success)
        {
            logger.LogError("REFERENCE CONTENT SEEDER: failed to publish Home content node: {Result}", result.Result);
            return;
        }

        logger.LogInformation("REFERENCE CONTENT SEEDER: created and published Home content node.");
    }
}
