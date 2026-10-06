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

    private static ServiceRequestPageViewModel StepWithStat(string? display, string? value = "52.2,0.1") => new()
    {
        Components =
        [
            new ComponentRenderPayload
            {
                Type = "stat-group",
                Stats = [new StatItem { Label = "Where", FieldKey = "location", Value = value, Display = display }],
            },
        ],
    };

    [Fact]
    public void AStepWithAMapTile_LoadsTheLocationScript_EvenWithNoPickerOnIt()
    {
        var step = StepWithStat("map");

        step.HasLocationMap.Should().BeTrue();
        step.NeedsLocationScript.Should().BeTrue();
        step.HasLocationPickerField.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text")]
    public void AStatTileThatIsNotAMap_DoesNotLoadTheLocationScript(string? display)
    {
        StepWithStat(display).NeedsLocationScript.Should().BeFalse();
    }

    [Fact]
    public void AStepWithAPicker_NeedsTheLocationScriptToo()
    {
        StepWith("location-picker").NeedsLocationScript.Should().BeTrue();
    }

    [Fact]
    public void AStepWithAFieldThatStartsOnTheDeviceClock_LoadsTheClockScript()
    {
        var step = new ServiceRequestPageViewModel
        {
            Components =
            [
                new ComponentRenderPayload
                {
                    Type = "fieldset",
                    Fields = [new FieldRenderPayload { FieldKey = "d", Label = "D", FieldType = "date", Required = false, DeviceDefault = "today" }],
                },
            ],
        };

        step.HasDeviceClockDefault.Should().BeTrue();
    }

    [Fact]
    public void AStepWithNoDeviceClockField_DoesNotLoadTheClockScript()
    {
        StepWith("text", "date").HasDeviceClockDefault.Should().BeFalse();
    }

    [Fact]
    public void ALocationPickerDoesNotTriggerTheFileUploadScript()
    {
        StepWith("location-picker").HasFileUploadField.Should().BeFalse();
    }
}
