using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Creates the <c>wayfinderServiceRequestStage</c> element type and a packaged
/// <c>Umbraco.BlockGrid</c> data type wrapping it, on package install — the Block Grid-composable
/// building block a CMS editor drags onto any content page to render one Wayfinder stage (see
/// <c>Controllers.WayfinderStageSurfaceController</c>/<c>Services.ServiceRequestStageService</c>
/// for the rendering/advance logic this block calls into). Ships via the package's own migration
/// plan (mirroring <see cref="CreateServiceBlueprintTable"/>/<see cref="CreateServiceRequestTable"/>'s
/// own DB-schema-on-install pattern) rather than a host-side dev-only seeder, so the block exists
/// the moment a site installs this NuGet package — no manual uSync import required.
/// </summary>
public class CreateServiceRequestStageBlock(
    IMigrationContext context,
    IContentTypeService contentTypeService,
    IDataTypeService dataTypeService,
    IShortStringHelper shortStringHelper,
    PropertyEditorCollection propertyEditorCollection,
    IConfigurationEditorJsonSerializer configurationEditorJsonSerializer)
    : AsyncMigrationBase(context)
{
    // Deterministic, fixed GUIDs — stable across every install of this package, so a re-run finds
    // and patches the same entities rather than creating duplicates.
    private static readonly Guid ElementTypeKey = new("6f2a1c3d-8b4e-4a1f-9c6d-2e7b5a9f1c30");
    private static readonly Guid BlockGridDataTypeKey = new("7a3b2d4e-9c5f-4b2a-8d7e-3f8c6b0a2d41");

    protected override async Task MigrateAsync()
    {
        var elementType = await GetOrCreateElementTypeAsync();
        await GetOrCreateBlockGridDataTypeAsync(elementType);
    }

    private async Task<IContentType> GetOrCreateElementTypeAsync()
    {
        if (contentTypeService.Get(ElementTypeKey) is { } existing)
        {
            return existing;
        }

        var elementType = new ContentType(shortStringHelper, -1)
        {
            Key = ElementTypeKey,
            Alias = "wayfinderServiceRequestStage",
            Name = "Wayfinder Service Request Stage",
            IsElement = true,
            Icon = "icon-molecule-alt"
        };

        var textBox = await dataTypeService.GetAsync(Constants.DataTypes.Guids.TextstringGuid)
            ?? throw new InvalidOperationException("Built-in Textstring data type not found.");

        const string groupName = "Stage";
        elementType.AddPropertyGroup(groupName, "stage");

        elementType.AddPropertyType(new PropertyType(shortStringHelper, textBox, "blueprintKey")
        {
            Name = "Blueprint key",
            Description = "The service blueprint definition key this block renders (e.g. \"apply-for-a-thing\").",
            Mandatory = true,
            SortOrder = 0
        }, groupName);

#pragma warning disable CS0618 // No non-deprecated Save overload on IContentTypeService in v17
        contentTypeService.Save(elementType);
#pragma warning restore CS0618

        return elementType;
    }

    private async Task GetOrCreateBlockGridDataTypeAsync(IContentType elementType)
    {
        if (await dataTypeService.GetAsync(BlockGridDataTypeKey) is not null)
        {
            return;
        }

        var editor = propertyEditorCollection["Umbraco.BlockGrid"]
            ?? throw new InvalidOperationException("Umbraco.BlockGrid editor not found in PropertyEditorCollection.");

        var dataType = new DataType(editor, configurationEditorJsonSerializer)
        {
            Key = BlockGridDataTypeKey,
            Name = "Wayfinder Service Request Stage (Block Grid)",
            DatabaseType = ValueStorageType.Ntext,
            EditorUiAlias = "Umb.PropertyEditorUi.BlockGrid",
            ConfigurationData = new Dictionary<string, object>
            {
                {
                    "blocks", new[]
                    {
                        new Dictionary<string, object>
                        {
                            { "contentElementTypeKey", elementType.Key },
                            { "allowAtRoot", true },
                            { "allowInAreas", true }
                        }
                    }
                }
            }
        };

        await dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey);
    }
}
