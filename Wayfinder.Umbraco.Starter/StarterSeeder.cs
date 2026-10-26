using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Wayfinder.Engine.Abstractions;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Starter;

/// <summary>
/// First boot only: the sample blueprint, and a home page if the site has no content yet. Runs
/// once per database, so whatever someone then edits or deletes stays that way. Registered after
/// Wayfinder.Umbraco's own startup migration, which creates the blueprint table this writes to.
/// </summary>
public class StarterSeeder(
    IRuntimeState runtimeState,
    IKeyValueService keyValueService,
    IServiceBlueprintSourceStore blueprintStore,
    IContentTypeService contentTypeService,
    ITemplateService templateService,
    IContentService contentService,
    IDataTypeService dataTypeService,
    IShortStringHelper shortStringHelper,
    ILogger<StarterSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string SeededKey = "Wayfinder.Umbraco.Starter.Seeded";

    public const string SampleBlueprintKey = "request-a-callback";

    public const string HomeAlias = "wayfinderStarterHome";

    private const string HomeTemplate = """
        @inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
        @{
            Layout = "~/Views/WayfinderStarter/_Layout.cshtml";
            ViewData["Title"] = Model.Name;
        }
        @await Html.PartialAsync("~/Views/WayfinderStarter/_Home.cshtml", Model)
        """;

    private const string DefaultIntroduction =
        "This site runs Wayfinder, a service blueprint engine for GOV.UK-style public services. " +
        "Try the sample service as a member of the public, then work the request as a caseworker. " +
        "Then connect an AI assistant and design a service of your own in conversation.";

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run || keyValueService.GetValue(SeededKey) is not null)
        {
            return;
        }

        await SeedSampleBlueprintAsync(cancellationToken);
        await SeedHomePageAsync();

        keyValueService.SetValue(SeededKey, DateTimeOffset.UtcNow.ToString("O"));
    }

    private async Task SeedSampleBlueprintAsync(CancellationToken cancellationToken)
    {
        if (await blueprintStore.LoadAsync(SampleBlueprintKey, cancellationToken) is not null)
        {
            return;
        }

        await using var yaml = typeof(StarterSeeder).Assembly.GetManifestResourceStream(
            $"{SampleBlueprintKey}.yaml")!;
        using var reader = new StreamReader(yaml);
        var blueprint = ServiceBlueprintYaml.Deserialize(await reader.ReadToEndAsync(cancellationToken));

        var result = await blueprintStore.SaveAsync(blueprint, expectedVersion: 0, cancellationToken);
        if (!result.Saved)
        {
            logger.LogError("Wayfinder starter: the sample blueprint {DefinitionKey} was not saved: {Result}", SampleBlueprintKey, result);
        }
    }

    private async Task SeedHomePageAsync()
    {
        // An existing site keeps its own home page; the starter's pages still work without one.
        if (contentService.GetRootContent().Any())
        {
            return;
        }

        var homeType = contentTypeService.Get(HomeAlias) ?? await CreateHomeTypeAsync();
        if (homeType is null)
        {
            return;
        }

        var home = contentService.Create("Home", Constants.System.Root, HomeAlias);
        home.SetValue("introduction", DefaultIntroduction);
        contentService.Save(home);
#pragma warning disable CS0618 // No non-obsolete Publish overload that takes a user id in v17
        var published = contentService.Publish(home, ["*"], Constants.Security.SuperUserId);
#pragma warning restore CS0618
        if (!published.Success)
        {
            logger.LogError("Wayfinder starter: the home page was not published: {Result}", published.Result);
        }
    }

    private async Task<IContentType?> CreateHomeTypeAsync()
    {
        var textarea = await dataTypeService.GetAsync(Constants.DataTypes.Guids.TextareaGuid);
        if (textarea is null)
        {
            logger.LogError("Wayfinder starter: Umbraco's Textarea data type is missing, so no home page was created.");
            return null;
        }

        var template = await templateService.GetAsync(HomeAlias)
            ?? (await templateService.CreateAsync("Wayfinder starter home", HomeAlias, HomeTemplate, Constants.Security.SuperUserKey)).Result;

        var homeType = new ContentType(shortStringHelper, Constants.System.Root)
        {
            Alias = HomeAlias,
            Name = "Wayfinder starter home",
            AllowedAsRoot = true,
            Icon = "icon-home",
            AllowedTemplates = [template],
        };
        homeType.SetDefaultTemplate(template);
        homeType.AddPropertyGroup("content", "Content");
        homeType.AddPropertyType(new PropertyType(shortStringHelper, textarea, "introduction")
        {
            Name = "Introduction",
            Description = "Shown at the top of the home page, above the list of services.",
        }, "content");

#pragma warning disable CS0618 // No non-obsolete Save overload on IContentTypeService in v17
        contentTypeService.Save(homeType);
#pragma warning restore CS0618
        return homeType;
    }
}
