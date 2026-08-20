using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Wayfinder.Umbraco.Persistence;

/// <summary>
/// Creates the <c>wayfinderServiceRequestWorklist</c> element type and a packaged
/// <c>Umbraco.BlockGrid</c> data type wrapping it — the caseworker/backstage counterpart to
/// <see cref="CreateServiceRequestStageBlock"/>'s citizen-facing stage block. A CMS editor drops
/// this block on any page to show the current actor's own queue work
/// (<see cref="Services.ServiceRequestWorklistService"/>) — unlike the stage block, it needs no
/// <c>blueprintKey</c> property: <c>IProcessManager.GetQueueWorkItems</c> is scoped entirely by
/// the resolved <c>ActorProfile</c>, not by a specific blueprint the block author picks.
/// </summary>
public class CreateServiceRequestWorklistBlock(
    IMigrationContext context,
    IContentTypeService contentTypeService,
    IDataTypeService dataTypeService,
    IShortStringHelper shortStringHelper,
    PropertyEditorCollection propertyEditorCollection,
    IConfigurationEditorJsonSerializer configurationEditorJsonSerializer)
    : AsyncMigrationBase(context)
{
    private static readonly Guid ElementTypeKey = new("8b4c3e5f-0d6a-4c3b-9e8f-4a9d7c1b3e52");
    private static readonly Guid BlockGridDataTypeKey = new("9c5d4f60-1e7b-4d4c-af90-5b0e8d2c4f63");

    protected override async Task MigrateAsync()
    {
        var elementType = GetOrCreateElementType();
        await GetOrCreateBlockGridDataTypeAsync(elementType);
    }

    private IContentType GetOrCreateElementType()
    {
        if (contentTypeService.Get(ElementTypeKey) is { } existing)
        {
            return existing;
        }

        var elementType = new ContentType(shortStringHelper, -1)
        {
            Key = ElementTypeKey,
            Alias = "wayfinderServiceRequestWorklist",
            Name = "Wayfinder Service Request Worklist",
            IsElement = true,
            Icon = "icon-list"
        };

        var numeric = dataTypeService.GetAsync(Constants.DataTypes.Guids.NumericGuid).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Built-in Numeric data type not found.");

        const string groupName = "Worklist";
        elementType.AddPropertyGroup(groupName, "worklist");

        elementType.AddPropertyType(new PropertyType(shortStringHelper, numeric, "pageSize")
        {
            Name = "Page size",
            Description = "How many rows to show per page. Leave blank to use the default (20).",
            Mandatory = false,
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

        var editor = propertyEditorCollection[Constants.PropertyEditors.Aliases.BlockGrid]
            ?? throw new InvalidOperationException("Umbraco.BlockGrid editor not found in PropertyEditorCollection.");

        var dataType = new DataType(editor, configurationEditorJsonSerializer)
        {
            Key = BlockGridDataTypeKey,
            Name = "Wayfinder Service Request Worklist (Block Grid)",
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
