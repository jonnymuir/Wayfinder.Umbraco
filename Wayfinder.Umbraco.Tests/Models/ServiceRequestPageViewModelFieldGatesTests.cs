using FluentAssertions;
using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Models;

namespace Wayfinder.Umbraco.Tests.Models;

/// <summary>
/// The question view loads a field type's script only on a step that actually renders that field.
/// </summary>
public sealed class ServiceRequestPageViewModelFieldGatesTests
{
    private static ServiceRequestPageViewModel StepWith(params string[] fieldTypes) => new()
    {
        Components =
        [
            new ComponentRenderPayload
            {
                Type = "fieldset",
                Fields = fieldTypes
                    .Select((type, i) => new FieldRenderPayload { FieldKey = $"f{i}", Label = "F", FieldType = type, Required = false })
                    .ToList(),
            },
        ],
    };

    [Fact]
    public void AStepWithALocationPicker_LoadsThePickerScript()
    {
        StepWith("text", "location-picker").HasLocationPickerField.Should().BeTrue();
    }

    [Fact]
    public void AStepWithoutALocationPicker_DoesNotLoadThePickerScript()
    {
        StepWith("text", "file-upload").HasLocationPickerField.Should().BeFalse();
    }

    [Fact]
    public void ALocationPickerDoesNotTriggerTheFileUploadScript()
    {
        StepWith("location-picker").HasFileUploadField.Should().BeFalse();
    }
}
